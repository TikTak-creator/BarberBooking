namespace BarberBooking.Web.Models;
public static class StatusText
{
    public static string Name(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Pending => "Ожидает подтверждения", AppointmentStatus.Confirmed => "Подтверждена",
        AppointmentStatus.Completed => "Визит завершён", AppointmentStatus.Cancelled => "Отменена", AppointmentStatus.NoShow => "Неявка", _ => "Неизвестно"
    };
}
