using System.ComponentModel.DataAnnotations;

namespace BarberBooking.Web.Models;

public class BookingRequestDto
{
    [Required(ErrorMessage = "Выберите услугу")]
    public int ServiceId { get; set; }

    [Required(ErrorMessage = "Выберите мастера")]
    public int BarberId { get; set; }

    [Required(ErrorMessage = "Выберите дату записи")]
    public DateTime Date { get; set; }

    [Required(ErrorMessage = "Выберите время записи")]
    public string TimeSlot { get; set; } = string.Empty; // e.g. "14:30"

    [Required(ErrorMessage = "Введите ваше имя")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Имя должно содержать от 2 до 100 символов")]
    public string ClientName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите номер телефона")]
    [RegularExpression(@"^(\+996|996|0)?[0-9\s\-()]{9,15}$", ErrorMessage = "Введите корректный номер телефона")]
    public string ClientPhone { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Введите корректный Email")]
    public string? ClientEmail { get; set; }

    [StringLength(300, ErrorMessage = "Комментарий не более 300 символов")]
    public string? Notes { get; set; }
}

public class TimeSlotDto
{
    public TimeSpan Time { get; set; }
    public string FormattedTime => Time.ToString(@"hh\:mm");
    public bool IsAvailable { get; set; }
    public string? Reason { get; set; }
}

public class CalendarAppointmentDto
{
    public int Id { get; set; }
    public string BookingCode { get; set; } = string.Empty;
    public int BarberId { get; set; }
    public string BarberName { get; set; } = string.Empty;
    public string BarberAvatar { get; set; } = string.Empty;
    public string BadgeColor { get; set; } = "#F59E0B";
    public int ServiceId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string ClientPhone { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime Date { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string TimeRange => $"{StartTime:hh\\:mm} – {EndTime:hh\\:mm}";
    public decimal Price { get; set; }
    public AppointmentStatus Status { get; set; }
    public string StatusName => Status switch
    {
        AppointmentStatus.Pending => "Ожидает",
        AppointmentStatus.Confirmed => "Подтверждена",
        AppointmentStatus.Completed => "Выполнена",
        AppointmentStatus.Cancelled => "Отменена",
        _ => Status.ToString()
    };
    public string StatusBadgeClass => Status switch
    {
        AppointmentStatus.Pending => "bg-warning text-dark",
        AppointmentStatus.Confirmed => "bg-primary text-white",
        AppointmentStatus.Completed => "bg-success text-white",
        AppointmentStatus.Cancelled => "bg-danger text-white",
        _ => "bg-secondary"
    };
}

public class AdminDashboardViewModel
{
    public int TodayAppointmentsCount { get; set; }
    public decimal TodayRevenue { get; set; }
    public int PendingCount { get; set; }
    public int TotalClientsCount { get; set; }
    public List<CalendarAppointmentDto> TodayAppointments { get; set; } = new();
    public List<Barber> ActiveBarbers { get; set; } = new();
    public List<ServiceItem> PopularServices { get; set; } = new();
}

public class ScheduleTimelineViewModel
{
    public DateTime SelectedDate { get; set; }
    public List<Barber> Barbers { get; set; } = new();
    public List<CalendarAppointmentDto> Appointments { get; set; } = new();
    public TimeSpan DayStart { get; set; } = new TimeSpan(10, 0, 0);
    public TimeSpan DayEnd { get; set; } = new TimeSpan(21, 0, 0);
}

public class BookingConfirmationViewModel
{
    public string BookingCode { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string ClientPhone { get; set; } = string.Empty;
    public string BarberName { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTime Date { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public AppointmentStatus Status { get; set; }
}
