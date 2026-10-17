using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BarberBooking.Web.Data;
using BarberBooking.Web.Models;
using Bladez.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BarberBooking.Web.Services;

public sealed class PrivateNormalizer(DataVault vault) : ILookupNormalizer
{
    public string? NormalizeName(string? name) => name?.Normalize().ToUpperInvariant();
    public string? NormalizeEmail(string? email) => string.IsNullOrWhiteSpace(email) ? null : vault.Fingerprint(email.Trim().ToUpperInvariant(), "Identity.EmailLookup");
}

public sealed class Actor(IHttpContextAccessor accessor)
{
    public ClaimsPrincipal User => accessor.HttpContext?.User ?? new ClaimsPrincipal();
    public string? Id => User.FindFirstValue(ClaimTypes.NameIdentifier);
    public bool IsOwner => User.IsInRole(Roles.Owner);
    public bool IsReception => User.IsInRole(Roles.Reception);
    public bool IsBarber => User.IsInRole(Roles.Barber);
    public bool IsStaff => IsOwner || IsReception || IsBarber;
    public int? BarberId => int.TryParse(User.FindFirstValue("barber_id"), out var id) ? id : null;
    public void RequireStaff() { if (!IsStaff || IsBarber && BarberId is null) throw new UnauthorizedAccessException(); }
    public void RequireManager() { if (!IsOwner && !IsReception) throw new UnauthorizedAccessException(); }
}

public sealed class Audit(BarberDbContext db, Actor actor)
{
    public void Add(string action, string target) => db.AuditEntries.Add(new AuditEntry { ActorId = actor.Id, Action = action, Target = target });
    public async Task WriteAsync(string action, string target) { Add(action, target); await db.SaveChangesAsync(); }
}

public sealed class ShopClock(TimeProvider time, BarberDbContext db)
{
    public async Task<ShopSettings> SettingsAsync() => await db.ShopSettings.AsNoTracking().SingleAsync(s => s.Id == 1);
    public DateTime Now(ShopSettings settings) => TimeZoneInfo.ConvertTimeFromUtc(time.GetUtcNow().UtcDateTime, TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId));
    public async Task<DateTime> NowAsync() => Now(await SettingsAsync());
}

public sealed class BookingAccess(IHttpContextAccessor accessor, IDataProtectionProvider protection, TimeProvider time)
{
    private readonly IDataProtector protector = protection.CreateProtector("Bladez.GuestBooking.v1");
    public static string Hash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    public bool CanRead(Appointment appointment)
    {
        var http = accessor.HttpContext;
        if (http is null) return false;
        var id = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is not null && appointment.CustomerUserId == id) return true;
        if (appointment.CustomerUserId is not null || appointment.TicketHash is null) return false;
        var cookie = http.Request.Cookies["Bladez.Booking." + appointment.BookingCode];
        if (string.IsNullOrEmpty(cookie)) return false;
        try
        {
            var parts = protector.Unprotect(cookie).Split('|');
            return parts.Length == 3 && parts[0] == appointment.BookingCode
                && long.TryParse(parts[1], out var expiry) && expiry > time.GetUtcNow().ToUnixTimeSeconds()
                && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[2]), Encoding.ASCII.GetBytes(appointment.TicketHash));
        }
        catch (CryptographicException) { return false; }
    }
    public bool Grant(Appointment appointment, string key)
    {
        if (appointment.CustomerUserId is not null || appointment.TicketHash is null || key.Length > 100) return false;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(key)), Encoding.ASCII.GetBytes(appointment.TicketHash))) return false;
        var http = accessor.HttpContext!; var expires = time.GetUtcNow().AddDays(90);
        http.Response.Cookies.Append("Bladez.Booking." + appointment.BookingCode,
            protector.Protect(appointment.BookingCode + "|" + expires.ToUnixTimeSeconds() + "|" + appointment.TicketHash),
            new CookieOptions { HttpOnly = true, Secure = http.Request.IsHttps, SameSite = SameSiteMode.Lax, IsEssential = true, Expires = expires, Path = "/Booking" });
        return true;
    }
}

public sealed class UserClaimsFactory(UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles,
    Microsoft.Extensions.Options.IOptions<IdentityOptions> options) : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(users, roles, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.BarberId.HasValue) identity.AddClaim(new Claim("barber_id", user.BarberId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return identity;
    }
}
