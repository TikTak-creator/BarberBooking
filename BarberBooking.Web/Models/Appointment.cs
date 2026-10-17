using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BarberBooking.Web.Models;

public class Appointment
{
    public int Id { get; set; }

    [Required]
    [StringLength(20)]
    public string BookingCode { get; set; } = string.Empty; // e.g. "BRB-4921"

    [Required]
    public int BarberId { get; set; }
    public Barber Barber { get; set; } = null!;

    [Required]
    public int ServiceId { get; set; }
    public ServiceItem Service { get; set; } = null!;

    [Required(ErrorMessage = "Введите имя клиента")]
    [StringLength(100)]
    public string ClientName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите номер телефона")]
    [StringLength(30)]
    public string ClientPhone { get; set; } = string.Empty;

    [StringLength(100)]
    [EmailAddress]
    public string? ClientEmail { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    [Required]
    public DateTime AppointmentDate { get; set; } // Date only part

    [Required]
    public TimeSpan StartTime { get; set; }

    [Required]
    public TimeSpan EndTime { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal Price { get; set; }

    public AppointmentStatus Status { get; set; } = AppointmentStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [StringLength(450)] public string? CustomerUserId { get; set; }
    public ApplicationUser? CustomerUser { get; set; }
    [StringLength(64)] public string? TicketHash { get; set; }
    [StringLength(64)] public string PhoneFingerprint { get; set; } = "";
    [StringLength(150)] public string ServiceNameSnapshot { get; set; } = "";
    public int DurationSnapshot { get; set; }
    [ConcurrencyCheck] public Guid Version { get; set; } = Guid.NewGuid();

    [StringLength(255)]
    public string? CancellationReason { get; set; }
}
