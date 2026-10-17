using System.ComponentModel.DataAnnotations;

namespace BarberBooking.Web.Models;

public class Barber
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Укажите имя мастера")]
    [StringLength(100)]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Укажите квалификацию / должность")]
    [StringLength(100)]
    public string Title { get; set; } = "Мастер";

    [StringLength(500)]
    public string Bio { get; set; } = string.Empty;

    [StringLength(255)]
    public string AvatarUrl { get; set; } = "/images/barber.svg";

    [StringLength(20)]
    public string BadgeColor { get; set; } = "#F59E0B"; // Hex color for calendar cards

    public double Rating { get; set; }

    public int ExperienceYears { get; set; }

    public bool IsActive { get; set; } = true;

    // Рабочие дни недели через запятую (0=Sunday, 1=Monday ... 6=Saturday)
    [StringLength(50)]
    public string WorkingDays { get; set; } = "1,2,3,4,5,6,0";

    public TimeSpan WorkStartTime { get; set; } = new TimeSpan(10, 0, 0);
    public TimeSpan WorkEndTime { get; set; } = new TimeSpan(21, 0, 0);

    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
}
