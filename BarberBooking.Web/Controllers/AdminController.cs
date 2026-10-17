using System.Data;
using System.Globalization;
using BarberBooking.Web.Data;
using BarberBooking.Web.Models;
using BarberBooking.Web.Services;
using Bladez.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BarberBooking.Web.Controllers;

[Authorize(Roles = Roles.Staff), Route("admin")]
public sealed class AdminController(IBookingService bookings, BarberDbContext db, Actor actor, ShopClock clock, Audit audit,
    UserManager<ApplicationUser> users, DataVault vault) : Controller
{
    private const string Managers = Roles.Owner + "," + Roles.Reception;
    [HttpGet(""), HttpGet("dashboard"), Authorize(Roles = Managers)]
    public async Task<IActionResult> Dashboard() => View(await bookings.GetAdminDashboardStatsAsync());
    [HttpGet("timeline")]
    public async Task<IActionResult> Timeline(DateTime? date)
    {
        var target = (date ?? await clock.NowAsync()).Date;
        ViewBag.Services = await bookings.GetActiveServicesAsync(); ViewBag.Manager = actor.IsOwner || actor.IsReception;
        return View(await bookings.GetScheduleTimelineAsync(target));
    }
    [HttpGet("appointments")]
    public async Task<IActionResult> Appointments(DateTime? fromDate, DateTime? toDate, int? barberId, AppointmentStatus? status, string? search)
    {
        ViewBag.Barbers = await bookings.GetActiveBarbersAsync(); ViewBag.Manager = actor.IsOwner || actor.IsReception;
        var from = (fromDate ?? (await clock.NowAsync()).Date.AddDays(-7)).Date; var to = (toDate ?? from.AddDays(30)).Date;
        ViewBag.FromDate = from.ToString("yyyy-MM-dd"); ViewBag.ToDate = to.ToString("yyyy-MM-dd"); ViewBag.SelectedBarberId = barberId; ViewBag.SelectedStatus = status; ViewBag.Search = search;
        try { return View(await bookings.GetFilteredAppointmentsAsync(from, to, barberId, status, search)); }
        catch (ArgumentException ex) { ModelState.AddModelError("", ex.Message); return View(new List<CalendarAppointmentDto>()); }
    }
    [HttpPost("update-status")]
    public async Task<IActionResult> UpdateStatus(int id, AppointmentStatus status, Guid version, string? returnUrl)
    {
        var result = await bookings.UpdateStatusAsync(id, status, version);
        TempData[result ? "SuccessMessage" : "ErrorMessage"] = result ? "Статус обновлён." : "Переход недоступен или запись уже изменилась. Обновите страницу.";
        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(nameof(Timeline));
    }
    [HttpPost("quick-create"), Authorize(Roles = Managers)]
    public async Task<IActionResult> QuickCreate(BookingRequestDto request)
    {
        if (!ModelState.IsValid) { TempData["ErrorMessage"] = "Проверьте поля формы."; return RedirectToAction(nameof(Timeline)); }
        try
        {
            var key = DataVault.RandomSecret(); var row = await bookings.CreateAppointmentAsync(request, key, forStaff: true);
            TempData["SuccessMessage"] = "Запись создана: " + row.BookingCode + ". Передайте клиенту секретный ключ: " + key;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or DoubleBookingConflictException) { TempData["ErrorMessage"] = ex.Message; }
        return RedirectToAction(nameof(Timeline), new { date = request.Date.ToString("yyyy-MM-dd") });
    }
    [HttpPost("reschedule"), Authorize(Roles = Managers)]
    public async Task<IActionResult> Reschedule(int id, DateTime date, string time, Guid version)
    {
        try
        {
            if (!TimeOnly.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)) throw new ArgumentException("Проверьте время.");
            var success = await bookings.RescheduleAsync(id, date, start.ToTimeSpan(), version);
            TempData[success ? "SuccessMessage" : "ErrorMessage"] = success ? "Запись перенесена. Сообщите клиенту новое время." : "Запись уже изменилась или недоступна.";
        }
        catch (Exception ex) when (ex is ArgumentException or DoubleBookingConflictException) { TempData["ErrorMessage"] = ex.Message; }
        return RedirectToAction(nameof(Timeline), new { date = date.ToString("yyyy-MM-dd") });
    }
    [HttpGet("barbers"), Authorize(Roles = Roles.Owner)]
    public async Task<IActionResult> Barbers() => View(await db.Barbers.AsNoTracking().OrderBy(b => b.Id).ToListAsync());
    [HttpGet("barbers/edit"), Authorize(Roles = Roles.Owner)]
    public async Task<IActionResult> EditBarber(int? id) => View(id.HasValue ? await db.Barbers.FindAsync(id.Value) ?? throw new KeyNotFoundException() : new Barber { AvatarUrl = "/images/barber.svg", Rating = 0 });
    [HttpPost("barbers/edit"), Authorize(Roles = Roles.Owner)]
    public async Task<IActionResult> EditBarber([Bind("Id,FullName,Title,Bio,BadgeColor,ExperienceYears,IsActive,WorkingDays,WorkStartTime,WorkEndTime")] Barber input)
    {
        var days = input.WorkingDays.Split(',').Select(s => int.TryParse(s.Trim(), out var day) ? day : -1).Distinct().Order().ToArray();
        if (days.Length == 0 || days.Any(d => d is < 0 or > 6) || input.WorkStartTime < TimeSpan.Zero || input.WorkEndTime >= TimeSpan.FromHours(24)
            || input.WorkStartTime.Ticks % TimeSpan.TicksPerMinute != 0 || input.WorkEndTime.Ticks % TimeSpan.TicksPerMinute != 0
            || input.WorkEndTime <= input.WorkStartTime || !System.Text.RegularExpressions.Regex.IsMatch(input.BadgeColor, "^#[0-9a-fA-F]{6}$") || input.ExperienceYears is < 0 or > 70)
            ModelState.AddModelError("", "Проверьте рабочие дни (0–6), смену, цвет и стаж.");
        if (!ModelState.IsValid) return View(input);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var row = input.Id == 0 ? new Barber { AvatarUrl = "/images/barber.svg", Rating = 0 } : await db.Barbers.SingleAsync(b => b.Id == input.Id);
        if (input.Id != 0)
        {
            await LockBarber(input.Id);
            var today = (await clock.NowAsync()).Date;
            var visits = await db.Appointments.Where(a => a.BarberId == input.Id && a.AppointmentDate >= today && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed))
                .Select(a => new { a.AppointmentDate, a.StartTime, a.EndTime }).ToListAsync();
            if (visits.Any(a => !input.IsActive || !days.Contains((int)a.AppointmentDate.DayOfWeek) || a.StartTime < input.WorkStartTime || a.EndTime > input.WorkEndTime))
            { ModelState.AddModelError("", "Новая смена конфликтует с будущими визитами. Сначала перенесите или отмените их."); return View(input); }
        }
        row.FullName = input.FullName.Trim(); row.Title = input.Title.Trim(); row.Bio = input.Bio.Trim(); row.BadgeColor = input.BadgeColor;
        row.ExperienceYears = input.ExperienceYears; row.IsActive = input.IsActive; row.WorkingDays = string.Join(',', days); row.WorkStartTime = input.WorkStartTime; row.WorkEndTime = input.WorkEndTime;
        if (input.Id == 0) db.Barbers.Add(row);
        audit.Add("catalog.barber_saved", input.Id.ToString()); await db.SaveChangesAsync(); await tx.CommitAsync();
        return RedirectToAction(nameof(Barbers));
    }
    [HttpGet("services"), Authorize(Roles = Roles.Owner)]
    public async Task<IActionResult> Services() => View(await db.Services.AsNoTracking().OrderBy(s => s.Id).ToListAsync());
    [HttpGet("services/edit"), Authorize(Roles = Roles.Owner)]
    public async Task<IActionResult> EditService(int? id) => View(id.HasValue ? await db.Services.FindAsync(id.Value) ?? throw new KeyNotFoundException() : new ServiceItem());
    [HttpPost("services/edit"), Authorize(Roles = Roles.Owner)]
    public async Task<IActionResult> EditService([Bind("Id,Name,Description,Price,DurationMinutes,Category,IsActive")] ServiceItem input)
    {
        if (input.Price is < 0 or > 1_000_000 || decimal.Round(input.Price, 2) != input.Price) ModelState.AddModelError("", "Цена должна быть от 0 до 1 000 000, до двух знаков после запятой.");
        if (!ModelState.IsValid) return View(input);
        var row = input.Id == 0 ? new ServiceItem() : await db.Services.SingleAsync(s => s.Id == input.Id);
        row.Name = input.Name.Trim(); row.Description = input.Description.Trim(); row.Price = input.Price; row.DurationMinutes = input.DurationMinutes;
        row.Category = input.Category.Trim(); row.IsActive = input.IsActive;
        if (input.Id == 0) db.Services.Add(row);
        audit.Add("catalog.service_saved", input.Id.ToString()); await db.SaveChangesAsync(); return RedirectToAction(nameof(Services));
    }
    [HttpGet("team"), Authorize(Roles = Roles.Owner)]
    public async Task<IActionResult> Team()
    {
        var list = await users.Users.AsNoTracking().OrderByDescending(u => u.CreatedUtc).Take(200).ToListAsync();
        var rows = new List<TeamRow>();
        foreach (var user in list) rows.Add(new TeamRow(user.Id, user.UserName!, user.DisplayName, user.IsActive, user.MustChangePassword, string.Join(", ", await users.GetRolesAsync(user))));
        ViewBag.Barbers = await bookings.GetActiveBarbersAsync(); ViewBag.CurrentId = actor.Id; ViewBag.OneTimePassword = TempData["OneTimePassword"];
        return View(rows);
    }
    [HttpPost("team/create"), Authorize(Roles = Roles.Owner)]
    public async Task<IActionResult> CreateStaff(StaffModel model)
    {
        if (model.Role is not (Roles.Reception or Roles.Barber) || model.Role == Roles.Barber && !await db.Barbers.AnyAsync(b => b.Id == model.BarberId && b.IsActive))
            ModelState.AddModelError("", "Выберите роль и мастера.");
        if (!ModelState.IsValid) { TempData["ErrorMessage"] = "Проверьте поля сотрудника и пароль (минимум 12 символов)."; return RedirectToAction(nameof(Team)); }
        var user = new ApplicationUser { UserName = model.UserName.Trim(), DisplayName = model.DisplayName.Trim(), BarberId = model.Role == Roles.Barber ? model.BarberId : null, MustChangePassword = true };
        var result = await users.CreateAsync(user, model.Password);
        if (!result.Succeeded) { TempData["ErrorMessage"] = string.Join(" ", result.Errors.Select(e => e.Description)); return RedirectToAction(nameof(Team)); }
        var roleResult = await users.AddToRoleAsync(user, model.Role);
        if (!roleResult.Succeeded) throw new InvalidOperationException("Role assignment failed.");
        await audit.WriteAsync("account.staff_created", user.Id); TempData["SuccessMessage"] = "Аккаунт сотрудника создан. При первом входе нужно изменить пароль.";
        return RedirectToAction(nameof(Team));
    }
    [HttpPost("team/toggle"), Authorize(Roles = Roles.Owner)]
    public async Task<IActionResult> ToggleUser(string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null || user.Id == actor.Id || await users.IsInRoleAsync(user, Roles.Owner)) return BadRequest();
        user.IsActive = !user.IsActive; await users.UpdateAsync(user); await users.UpdateSecurityStampAsync(user);
        await audit.WriteAsync(user.IsActive ? "account.enabled" : "account.disabled", user.Id); return RedirectToAction(nameof(Team));
    }
    [HttpPost("team/reset-password"), Authorize(Roles = Roles.Owner)]
    public async Task<IActionResult> ResetUserPassword(string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null || await users.IsInRoleAsync(user, Roles.Owner)) return BadRequest();
        var password = "Aa1-" + DataVault.RandomSecret(18);
        var result = await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), password);
        if (!result.Succeeded) return BadRequest();
        user.MustChangePassword = true; await users.UpdateAsync(user); await users.UpdateSecurityStampAsync(user);
        TempData["OneTimePassword"] = user.UserName + ": " + password;
        await audit.WriteAsync("account.password_reset_by_owner", user.Id); return RedirectToAction(nameof(Team));
    }
    [HttpGet("time-off"), Authorize(Roles = Managers)]
    public async Task<IActionResult> TimeOff()
    {
        var today = (await clock.NowAsync()).Date;
        ViewBag.Barbers = await bookings.GetActiveBarbersAsync();
        return View(await db.TimeOff.AsNoTracking().Include(t => t.Barber).Where(t => t.Date >= today).OrderBy(t => t.Date).ThenBy(t => t.StartTime).Take(200).ToListAsync());
    }
    [HttpPost("time-off"), Authorize(Roles = Managers)]
    public async Task<IActionResult> AddTimeOff(int barberId, DateTime date, string start, string end, string? reason)
    {
        var today = (await clock.NowAsync()).Date;
        if (!TimeOnly.TryParseExact(start, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var a)
            || !TimeOnly.TryParseExact(end, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var b)
            || b <= a || date.Date < today || date.Date > today.AddDays(180) || reason?.Length > 200)
        { TempData["ErrorMessage"] = "Проверьте дату, время и причину перерыва."; return RedirectToAction(nameof(TimeOff)); }
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted); await LockBarber(barberId);
        var from = a.ToTimeSpan(); var to = b.ToTimeSpan(); var day = date.Date;
        if (await db.Appointments.AnyAsync(x => x.BarberId == barberId && x.AppointmentDate == day && x.Status != AppointmentStatus.Cancelled && x.StartTime < to && x.EndTime > from))
        { TempData["ErrorMessage"] = "Перерыв пересекается с записью клиента. Сначала перенесите визит."; return RedirectToAction(nameof(TimeOff)); }
        db.TimeOff.Add(new TimeOff { BarberId = barberId, Date = day, StartTime = from, EndTime = to, Reason = string.IsNullOrWhiteSpace(reason) ? "Перерыв" : reason.Trim() });
        audit.Add("schedule.time_off_added", barberId.ToString()); await db.SaveChangesAsync(); await tx.CommitAsync(); return RedirectToAction(nameof(TimeOff));
    }
    [HttpPost("time-off/remove"), Authorize(Roles = Managers)]
    public async Task<IActionResult> RemoveTimeOff(int id)
    {
        var row = await db.TimeOff.FindAsync(id); if (row is null) return NotFound();
        db.TimeOff.Remove(row); audit.Add("schedule.time_off_removed", id.ToString()); await db.SaveChangesAsync(); return RedirectToAction(nameof(TimeOff));
    }
    [HttpGet("settings"), Authorize(Roles = Roles.Owner)] public async Task<IActionResult> Settings() => View(await clock.SettingsAsync());
    [HttpPost("settings"), Authorize(Roles = Roles.Owner)]
    public async Task<IActionResult> Settings([Bind("Name,Address,Phone,TimeZoneId,OnlineBookingEnabled,AllowGuestBooking,BookingHorizonDays,LeadMinutes,CancellationLeadMinutes,SmtpHost,SmtpPort,SmtpUser,SmtpFrom")] ShopSettings input, string? smtpPassword)
    {
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(input.TimeZoneId); } catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { ModelState.AddModelError("", "Укажите существующий часовой пояс IANA."); }
        if (smtpPassword?.Length > 500) ModelState.AddModelError("", "Слишком длинный пароль SMTP.");
        if (!ModelState.IsValid) return View(input);
        var row = await db.ShopSettings.SingleAsync(s => s.Id == 1);
        if (row.TimeZoneId != input.TimeZoneId && await db.Appointments.AnyAsync(a => a.AppointmentDate >= DateTime.UtcNow.AddDays(-1).Date && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed)))
        { ModelState.AddModelError("", "Не меняйте часовой пояс при наличии будущих визитов. Сначала перенесите или отмените записи."); return View(input); }
        row.Name = input.Name.Trim(); row.Address = input.Address?.Trim() ?? ""; row.Phone = input.Phone?.Trim() ?? ""; row.TimeZoneId = input.TimeZoneId;
        row.OnlineBookingEnabled = input.OnlineBookingEnabled; row.AllowGuestBooking = input.AllowGuestBooking; row.BookingHorizonDays = input.BookingHorizonDays;
        row.LeadMinutes = input.LeadMinutes; row.CancellationLeadMinutes = input.CancellationLeadMinutes;
        row.SmtpHost = input.SmtpHost?.Trim(); row.SmtpPort = input.SmtpPort; row.SmtpUser = input.SmtpUser?.Trim(); row.SmtpFrom = input.SmtpFrom?.Trim();
        if (!string.IsNullOrEmpty(smtpPassword))
        {
            var secrets = System.IO.File.Exists(Path.Combine(vault.DirectoryPath, "secrets.vault")) ? vault.ReadSecrets() : new HostSecrets();
            secrets.SmtpPassword = smtpPassword; vault.SaveSecrets(secrets);
        }
        audit.Add("shop.settings_updated", "shop"); await db.SaveChangesAsync(); TempData["SuccessMessage"] = "Настройки сохранены."; return RedirectToAction(nameof(Settings));
    }
    [HttpGet("audit"), Authorize(Roles = Roles.Owner)]
    public async Task<IActionResult> AuditLog() => View(await db.AuditEntries.AsNoTracking().OrderByDescending(a => a.Id).Take(200).ToListAsync());
    [HttpGet("system"), Authorize(Roles = Roles.Owner)]
    public async Task<IActionResult> SystemStatus()
    {
        ViewBag.Database = await db.Database.CanConnectAsync(); ViewBag.ServerUtc = DateTime.UtcNow; ViewBag.Runtime = Environment.Version.ToString();
        ViewBag.SchemaPending = (await db.Database.GetPendingMigrationsAsync()).Count(); return View();
    }
    private async Task LockBarber(int id)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand(); command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "SELECT Id FROM Barbers WHERE Id = @id FOR UPDATE";
        var parameter = command.CreateParameter(); parameter.ParameterName = "@id"; parameter.Value = id; command.Parameters.Add(parameter);
        if (await command.ExecuteScalarAsync() is null) throw new ArgumentException("Мастер не найден.");
    }
}
public sealed record TeamRow(string Id, string Login, string Name, bool Active, bool MustChangePassword, string Roles);
