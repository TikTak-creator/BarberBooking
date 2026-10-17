namespace BarberBooking.Web.Models;

public enum AppointmentStatus
{
    Pending = 0,    // Ожидает подтверждения
    Confirmed = 1,  // Подтверждена администратором
    Completed = 2,  // Выполнена
    Cancelled = 3,
    NoShow = 4
}
