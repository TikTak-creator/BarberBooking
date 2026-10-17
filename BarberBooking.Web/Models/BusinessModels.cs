using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace BarberBooking.Web.Models;

public static class Roles
{
    public const string Owner = "Owner", Reception = "Reception", Barber = "Barber", Customer = "Customer";
    public const string Staff = Owner + "," + Reception + "," + Barber;
}

public sealed class ApplicationUser : IdentityUser
{
    [StringLength(100)] public string DisplayName { get; set; } = "";
    public int? BarberId { get; set; }
    public Barber? Barber { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ShopSettings
{
    public int Id { get; set; } = 1;
    [Required, StringLength(100)] public string Name { get; set; } = "Bladez";
    [StringLength(200)] public string Address { get; set; } = "";
    [StringLength(40)] public string Phone { get; set; } = "";
    [Required, StringLength(80)] public string TimeZoneId { get; set; } = "Asia/Bishkek";
    public bool OnlineBookingEnabled { get; set; } = true;
    public bool AllowGuestBooking { get; set; }
    [StringLength(64)] public string DataKeyId { get; set; } = "";
    [Range(1, 180)] public int BookingHorizonDays { get; set; } = 60;
    [Range(0, 240)] public int LeadMinutes { get; set; } = 30;
    [Range(0, 1440)] public int CancellationLeadMinutes { get; set; } = 120;
    [StringLength(100)] public string? SmtpHost { get; set; }
    [Range(1, 65535)] public int SmtpPort { get; set; } = 587;
    [StringLength(100)] public string? SmtpUser { get; set; }
    [EmailAddress, StringLength(100)] public string? SmtpFrom { get; set; }
}

public sealed class TimeOff
{
    public int Id { get; set; }
    public int BarberId { get; set; }
    public Barber Barber { get; set; } = null!;
    public DateTime Date { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    [StringLength(200)] public string Reason { get; set; } = "Перерыв";
}

public sealed class AuditEntry
{
    public long Id { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    [StringLength(64)] public string? ActorId { get; set; }
    [StringLength(80)] public string Action { get; set; } = "";
    [StringLength(100)] public string Target { get; set; } = "";
}

public sealed class LoginModel
{
    [Required, StringLength(50)] public string UserName { get; set; } = "";
    [Required, StringLength(128), DataType(DataType.Password)] public string Password { get; set; } = "";
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public sealed class RegisterModel
{
    [Required, RegularExpression("^[a-zA-Z0-9._-]{3,40}$", ErrorMessage = "Логин: 3–40 латинских букв, цифр или ._-.")]
    public string UserName { get; set; } = "";
    [Required, StringLength(100, MinimumLength = 2)] public string DisplayName { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Compare(nameof(Password)), DataType(DataType.Password)] public string ConfirmPassword { get; set; } = "";
    [EmailAddress, StringLength(100)] public string? Email { get; set; }
    public bool AcceptPrivacy { get; set; }
}

public sealed class PasswordModel
{
    [Required, DataType(DataType.Password), StringLength(128)] public string CurrentPassword { get; set; } = "";
    [Required, DataType(DataType.Password), StringLength(128, MinimumLength = 12)] public string NewPassword { get; set; } = "";
    [Compare(nameof(NewPassword)), DataType(DataType.Password)] public string ConfirmPassword { get; set; } = "";
}

public sealed class TwoFactorModel
{
    [Required, RegularExpression("^[0-9 ]{6,8}$")] public string Code { get; set; } = "";
    public string? ReturnUrl { get; set; }
    public bool RememberMe { get; set; }
}

public sealed class StaffModel
{
    [Required, RegularExpression("^[a-zA-Z0-9._-]{3,40}$")] public string UserName { get; set; } = "";
    [Required, StringLength(100)] public string DisplayName { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12)] public string Password { get; set; } = "";
    [Required] public string Role { get; set; } = Roles.Reception;
    public int? BarberId { get; set; }
}

public sealed class BootstrapModel
{
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Password { get; set; } = "";
    public string ShopName { get; set; } = "Bladez";
    public bool SeedCatalog { get; set; } = true;
}
