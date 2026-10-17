using System.Security.Claims;
using System.Text;
using BarberBooking.Web.Data;
using BarberBooking.Web.Models;
using BarberBooking.Web.Services;
using Bladez.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace BarberBooking.Web.Controllers;

[Authorize]
public sealed class AccountController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    IPasswordHasher<ApplicationUser> hasher, Audit audit, IBookingService booking, AccountMail mail, HostSettings host) : Controller
{
    private async Task<ApplicationUser> Current() => await users.GetUserAsync(User) ?? throw new UnauthorizedAccessException();
    private IActionResult Destination(string? url = null) => !string.IsNullOrEmpty(url) && Url.IsLocalUrl(url) ? LocalRedirect(url)
        : User.IsInRole(Roles.Barber) ? RedirectToAction("Timeline", "Admin")
        : User.IsInRole(Roles.Owner) || User.IsInRole(Roles.Reception) ? RedirectToAction("Dashboard", "Admin") : RedirectToAction(nameof(MyBookings));
    private void Errors(IdentityResult result) { foreach (var error in result.Errors) ModelState.AddModelError("", error.Description); }

    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl) => User.Identity?.IsAuthenticated == true ? Destination(returnUrl) : View(new LoginModel { ReturnUrl = returnUrl });

    [AllowAnonymous, HttpPost, EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var user = await users.FindByNameAsync(model.UserName.Trim());
        if (user is null || !user.IsActive)
        {
            _ = hasher.HashPassword(new ApplicationUser(), model.Password);
            ModelState.AddModelError("", "Вход не выполнен. Проверьте данные или повторите позже."); return View(model);
        }
        var result = await signIn.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: true);
        if (result.RequiresTwoFactor) return RedirectToAction(nameof(TwoFactor), new { model.ReturnUrl, model.RememberMe });
        if (result.Succeeded)
        {
            await audit.WriteAsync("account.login", user.Id);
            return !string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl)
                : RedirectToAction(nameof(Home));
        }
        await audit.WriteAsync("account.login_failed", user.Id);
        ModelState.AddModelError("", "Вход не выполнен. Проверьте данные или повторите позже."); return View(model);
    }

    [HttpGet] public IActionResult Home() => Destination();
    [HttpPost] public async Task<IActionResult> Logout() { await signIn.SignOutAsync(); return RedirectToAction("Index", "Home"); }

    [AllowAnonymous, HttpGet] public IActionResult Register() => View(new RegisterModel());
    [AllowAnonymous, HttpPost, EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(RegisterModel model)
    {
        if (!model.AcceptPrivacy) ModelState.AddModelError(nameof(model.AcceptPrivacy), "Ознакомьтесь с обработкой данных и подтвердите согласие.");
        if (!ModelState.IsValid) return View(model);
        var user = new ApplicationUser { UserName = model.UserName.Trim(), DisplayName = model.DisplayName.Trim(), Email = model.Email?.Trim() };
        if (!string.IsNullOrEmpty(user.Email) && await users.FindByEmailAsync(user.Email) is not null)
        { ModelState.AddModelError("", "Не удалось создать аккаунт с этими данными. Используйте восстановление доступа или другой email."); return View(model); }
        var result = await users.CreateAsync(user, model.Password);
        if (!result.Succeeded) { Errors(result); return View(model); }
        var role = await users.AddToRoleAsync(user, Roles.Customer);
        if (!role.Succeeded) throw new InvalidOperationException("Customer role is unavailable.");
        await audit.WriteAsync("account.registered", user.Id);
        await signIn.SignInAsync(user, false);
        return RedirectToAction(nameof(MyBookings));
    }

    [HttpGet] public async Task<IActionResult> MyBookings() => View(await booking.GetMyAppointmentsAsync());

    [HttpGet] public async Task<IActionResult> Profile() => View(await Current());
    [HttpPost]
    public async Task<IActionResult> Profile([FromForm] string displayName, [FromForm] string? email, [FromForm] string? phone)
    {
        var user = await Current();
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length is < 2 or > 100 || email?.Length > 100
            || !string.IsNullOrWhiteSpace(email) && !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
        { ModelState.AddModelError("", "Проверьте имя и email."); return View(user); }
        string? normalizedPhone = null;
        if (!string.IsNullOrWhiteSpace(phone))
        {
            try { normalizedPhone = BookingService.NormalizePhone(phone); }
            catch (ArgumentException) { ModelState.AddModelError("", "Проверьте телефон."); return View(user); }
        }
        var newEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (!string.Equals(user.Email, newEmail, StringComparison.OrdinalIgnoreCase))
        {
            var existing = newEmail is null ? null : await users.FindByEmailAsync(newEmail);
            if (existing is not null && existing.Id != user.Id) { ModelState.AddModelError("", "Этот email недоступен."); return View(user); }
            user.Email = newEmail; user.EmailConfirmed = false;
        }
        user.DisplayName = displayName.Trim(); user.PhoneNumber = normalizedPhone; user.PhoneNumberConfirmed = false;
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded) { Errors(result); return View(user); }
        await users.UpdateSecurityStampAsync(user); await signIn.RefreshSignInAsync(user);
        await audit.WriteAsync("account.profile_updated", user.Id);
        TempData["SuccessMessage"] = "Профиль сохранён. Телефон не считается проверенным без SMS-подтверждения.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpGet] public IActionResult ChangePassword() => View(new PasswordModel());
    [HttpPost, EnableRateLimiting("auth")]
    public async Task<IActionResult> ChangePassword(PasswordModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var user = await Current();
        var result = await users.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded) { Errors(result); return View(model); }
        user.MustChangePassword = false; await users.UpdateAsync(user);
        await users.UpdateSecurityStampAsync(user); await signIn.RefreshSignInAsync(user);
        await audit.WriteAsync("account.password_changed", user.Id);
        TempData["SuccessMessage"] = "Пароль изменён. Остальные сеансы завершены.";
        return RedirectToAction(nameof(Security));
    }

    [HttpGet]
    public async Task<IActionResult> Security()
    {
        var user = await Current(); ViewBag.Enabled = await users.GetTwoFactorEnabledAsync(user); ViewBag.Codes = TempData["RecoveryCodes"] as string;
        return View(user);
    }
    [HttpPost, EnableRateLimiting("auth")]
    public async Task<IActionResult> BeginTwoFactor(string password)
    {
        var user = await Current();
        if (password?.Length is not (>= 1 and <= 128) || !await users.CheckPasswordAsync(user, password) || await users.GetTwoFactorEnabledAsync(user))
        { TempData["ErrorMessage"] = "Проверьте пароль и состояние двухэтапного входа."; return RedirectToAction(nameof(Security)); }
        await users.ResetAuthenticatorKeyAsync(user); await users.UpdateSecurityStampAsync(user); await signIn.RefreshSignInAsync(user);
        return RedirectToAction(nameof(EnrollTwoFactor));
    }
    [HttpGet]
    public async Task<IActionResult> EnrollTwoFactor()
    {
        var user = await Current(); var key = await users.GetAuthenticatorKeyAsync(user);
        if (key is null || await users.GetTwoFactorEnabledAsync(user)) return RedirectToAction(nameof(Security));
        var uri = "otpauth://totp/" + Uri.EscapeDataString("Bladez:" + user.UserName) + "?secret=" + Uri.EscapeDataString(key) + "&issuer=Bladez&digits=6";
        using var generator = new QRCodeGenerator(); using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q); using var qr = new SvgQRCode(data);
        ViewBag.Qr = qr.GetGraphic(5); ViewBag.Key = key; return View(new TwoFactorModel());
    }
    [HttpPost, EnableRateLimiting("auth")]
    public async Task<IActionResult> EnableTwoFactor(TwoFactorModel model)
    {
        var user = await Current();
        if (!ModelState.IsValid || !await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, model.Code.Replace(" ", "")))
        { TempData["ErrorMessage"] = "Код не подошёл. Проверьте время в приложении-аутентификаторе."; return RedirectToAction(nameof(EnrollTwoFactor)); }
        await users.SetTwoFactorEnabledAsync(user, true); await users.UpdateSecurityStampAsync(user); await signIn.RefreshSignInAsync(user);
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        TempData["RecoveryCodes"] = string.Join("\n", codes ?? []); await audit.WriteAsync("account.2fa_enabled", user.Id);
        return RedirectToAction(nameof(Security));
    }
    [HttpPost, EnableRateLimiting("auth")]
    public async Task<IActionResult> DisableTwoFactor(string password, string code)
    {
        var user = await Current();
        if (password?.Length is not (>= 1 and <= 128) || code?.Length is not (>= 6 and <= 8) || !await users.CheckPasswordAsync(user, password)
            || !await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code.Replace(" ", "")))
        { TempData["ErrorMessage"] = "Нужны текущий пароль и действующий код."; return RedirectToAction(nameof(Security)); }
        await users.SetTwoFactorEnabledAsync(user, false); await users.ResetAuthenticatorKeyAsync(user); await users.UpdateSecurityStampAsync(user); await signIn.RefreshSignInAsync(user);
        await audit.WriteAsync("account.2fa_disabled", user.Id); return RedirectToAction(nameof(Security));
    }

    [AllowAnonymous, HttpGet]
    public IActionResult TwoFactor(string? returnUrl, bool rememberMe) => View(new TwoFactorModel { ReturnUrl = returnUrl, RememberMe = rememberMe });
    [AllowAnonymous, HttpPost, EnableRateLimiting("auth")]
    public async Task<IActionResult> TwoFactor(TwoFactorModel model)
    {
        var user = await signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null || !user.IsActive) return RedirectToAction(nameof(Login));
        if (ModelState.IsValid)
        {
            var result = await signIn.TwoFactorAuthenticatorSignInAsync(model.Code.Replace(" ", ""), model.RememberMe, false);
            if (result.Succeeded) { await audit.WriteAsync("account.2fa_login", user.Id); return !string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl) : RedirectToAction(nameof(Home)); }
        }
        ModelState.AddModelError("", "Вход не выполнен. Проверьте код или повторите позже."); return View(model);
    }
    [AllowAnonymous, HttpPost, EnableRateLimiting("auth")]
    public async Task<IActionResult> RecoveryLogin(string code)
    {
        var user = await signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null || !user.IsActive) return RedirectToAction(nameof(Login));
        if (code?.Length is >= 5 and <= 50 && (await signIn.TwoFactorRecoveryCodeSignInAsync(code.Replace(" ", ""))).Succeeded)
        { await audit.WriteAsync("account.recovery_code_used", user.Id); return RedirectToAction(nameof(Home)); }
        TempData["ErrorMessage"] = "Резервный код не подошёл или уже использован."; return RedirectToAction(nameof(TwoFactor));
    }

    [HttpPost, EnableRateLimiting("auth")]
    public async Task<IActionResult> SendConfirmation()
    {
        var user = await Current();
        if (!string.IsNullOrEmpty(user.Email) && !user.EmailConfirmed)
        {
            var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GenerateEmailConfirmationTokenAsync(user)));
            var sent = await mail.SendAsync(user.Email, "Bladez — подтверждение email", $"Откройте {host.PublicUrl.TrimEnd('/')}/Account/ConfirmEmail\nЛогин: {user.UserName}\nКод подтверждения (30 минут):\n{token}\nЕсли это не ваш запрос, проигнорируйте письмо.");
            TempData["SuccessMessage"] = sent ? "Письмо отправлено. Код действует 30 минут." : "Почта не настроена или письмо не доставлено. Свяжитесь с администратором.";
        }
        return RedirectToAction(nameof(Profile));
    }
    [AllowAnonymous, HttpGet] public IActionResult ConfirmEmail() => View();
    [AllowAnonymous, HttpPost, EnableRateLimiting("auth")]
    public async Task<IActionResult> ConfirmEmail(string userName, string token)
    {
        if (userName?.Length <= 40 && token?.Length is > 0 and < 3000)
        {
            var user = await users.FindByNameAsync(userName);
            try
            {
                if (user is { IsActive: true } && (await users.ConfirmEmailAsync(user, Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token)))).Succeeded)
                { TempData["SuccessMessage"] = "Email подтверждён."; return RedirectToAction(nameof(Login)); }
            }
            catch (FormatException) { }
        }
        ModelState.AddModelError("", "Код не подошёл или истёк."); return View();
    }

    [AllowAnonymous, HttpGet] public IActionResult ForgotPassword() => View();
    [AllowAnonymous, HttpPost, EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword(string userName)
    {
        if (userName?.Length is > 0 and <= 40)
        {
            var user = await users.FindByNameAsync(userName);
            if (user is { IsActive: true, EmailConfirmed: true, Email: not null })
            {
                var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GeneratePasswordResetTokenAsync(user)));
                await mail.SendAsync(user.Email, "Bladez — восстановление доступа", $"Откройте {host.PublicUrl.TrimEnd('/')}/Account/ResetPassword\nЛогин: {user.UserName}\nОдноразовый код (30 минут):\n{token}\nЕсли вы не запрашивали восстановление, не передавайте код и проигнорируйте письмо.");
            }
        }
        TempData["SuccessMessage"] = "Если аккаунт доступен, email подтверждён и почта настроена, инструкция отправлена. Иначе свяжитесь с администратором салона.";
        return RedirectToAction(nameof(ForgotPassword));
    }
    [AllowAnonymous, HttpGet] public IActionResult ResetPassword() => View();
    [AllowAnonymous, HttpPost, EnableRateLimiting("auth")]
    public async Task<IActionResult> ResetPassword(string userName, string token, string password, string confirmPassword)
    {
        if (userName?.Length is > 0 and <= 40 && token?.Length is > 0 and < 3000 && password?.Length is >= 12 and <= 128 && password == confirmPassword)
        {
            var user = await users.FindByNameAsync(userName);
            try
            {
                if (user is { IsActive: true } && (await users.ResetPasswordAsync(user, Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token)), password)).Succeeded)
                {
                    user.MustChangePassword = false; await users.UpdateAsync(user); await users.UpdateSecurityStampAsync(user);
                    await audit.WriteAsync("account.password_reset", user.Id); TempData["SuccessMessage"] = "Пароль обновлён. Войдите заново."; return RedirectToAction(nameof(Login));
                }
            }
            catch (FormatException) { }
        }
        ModelState.AddModelError("", "Проверьте логин, код и пароль (12–128 символов, с цифрой, строчными и заглавными буквами)."); return View();
    }
    [AllowAnonymous, HttpGet] public IActionResult Denied() { Response.StatusCode = 403; return View(); }
}
