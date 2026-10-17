using BarberBooking.Web.Models;
namespace BarberBooking.Web.Services;
public interface IBookingService
{
    Task<List<Barber>> GetActiveBarbersAsync();
    Task<List<ServiceItem>> GetActiveServicesAsync();
    Task<List<TimeSlotDto>> GetAvailableSlotsAsync(int barberId, int serviceId, DateTime date);
    Task<Appointment> CreateAppointmentAsync(BookingRequestDto request, string? guestKey = null, bool forStaff = false);
    Task<Appointment?> GetAppointmentByCodeAsync(string code);
    Task<List<Appointment>> GetMyAppointmentsAsync();
    Task<ScheduleTimelineViewModel> GetScheduleTimelineAsync(DateTime date);
    Task<AdminDashboardViewModel> GetAdminDashboardStatsAsync();
    Task<List<CalendarAppointmentDto>> GetFilteredAppointmentsAsync(DateTime? fromDate, DateTime? toDate, int? barberId, AppointmentStatus? status, string? search);
    Task<bool> UpdateStatusAsync(int id, AppointmentStatus status, Guid version);
    Task<bool> RescheduleAsync(int id, DateTime date, TimeSpan start, Guid version);
    Task<bool> CancelByClientAsync(string code, string reason, Guid version);
}
