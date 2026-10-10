using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BarberBooking.Web.Models;

public class ServiceItem
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Укажите название услуги")]
    [StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    [Required]
    [Column(TypeName = "decimal(12,2)")]
    public decimal Price { get; set; }

    [Required]
    [Range(10, 240, ErrorMessage = "Длительность должна быть от 10 до 240 минут")]
    public int DurationMinutes { get; set; } = 45;

    [StringLength(100)]
    public string Category { get; set; } = "Стрижки";

    [StringLength(50)]
    public string Icon { get; set; } = "bi-scissors";

    public bool IsActive { get; set; } = true;

    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
}
