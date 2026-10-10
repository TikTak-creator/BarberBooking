using BarberBooking.Web.Models;

namespace BarberBooking.Web.Services;

public interface IBookingService
{
    Task<List<Barber>> GetActiveBarbersAsync();
    Task<Barber?> GetBarberByIdAsync(int id);
    Task<List<ServiceItem>> GetActiveServicesAsync();
    Task<ServiceItem?> GetServiceByIdAsync(int id);
    Task<List<TimeSlotDto>> GetAvailableSlotsAsync(int barberId, int serviceId, DateTime date);
    Task<Appointment> CreateAppointmentAsync(BookingRequestDto request);
    Task<Appointment?> GetAppointmentByCodeAsync(string bookingCode);
    Task<List<Appointment>> GetAppointmentsByPhoneAsync(string phone);
    Task<ScheduleTimelineViewModel> GetScheduleTimelineAsync(DateTime date);
    Task<AdminDashboardViewModel> GetAdminDashboardStatsAsync();
    Task<List<CalendarAppointmentDto>> GetFilteredAppointmentsAsync(DateTime? fromDate, DateTime? toDate, int? barberId, AppointmentStatus? status, string? search);
    Task<bool> UpdateStatusAsync(int id, AppointmentStatus status, string? reason = null);
    Task<bool> RescheduleAsync(int id, DateTime newDate, TimeSpan newStartTime);
    Task<bool> CancelByClientAsync(string bookingCode, string reason);
}
