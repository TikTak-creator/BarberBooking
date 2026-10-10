using BarberBooking.Web.Models;
using BarberBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace BarberBooking.Web.Controllers;

[Route("admin")]
public class AdminController : Controller
{
    private readonly IBookingService _bookingService;
    private readonly ILogger<AdminController> _logger;

    public AdminController(IBookingService bookingService, ILogger<AdminController> logger)
    {
        _bookingService = bookingService;
        _logger = logger;
    }

    // Главная страница дашборда администратора
    [HttpGet("")]
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var model = await _bookingService.GetAdminDashboardStatsAsync();
        return View(model);
    }

    // Интерактивный таймлайн-календарь расписания (как на референсном скриншоте)
    [HttpGet("timeline")]
    public async Task<IActionResult> Timeline(DateTime? date)
    {
        var targetDate = date ?? DateTime.Today;
        var model = await _bookingService.GetScheduleTimelineAsync(targetDate);
        ViewBag.Services = await _bookingService.GetActiveServicesAsync();
        return View(model);
    }

    // Список всех записей с фильтрацией и поиском
    [HttpGet("appointments")]
    public async Task<IActionResult> Appointments(
        DateTime? fromDate, DateTime? toDate, int? barberId, AppointmentStatus? status, string? search)
    {
        var list = await _bookingService.GetFilteredAppointmentsAsync(fromDate, toDate, barberId, status, search);
        ViewBag.Barbers = await _bookingService.GetActiveBarbersAsync();
        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
        ViewBag.SelectedBarberId = barberId;
        ViewBag.SelectedStatus = status;
        ViewBag.Search = search;
        return View(list);
    }

    // Смена статуса записи (Подтверждена, Выполнена, Отменена)
    [HttpPost("update-status")]
    public async Task<IActionResult> UpdateStatus([FromForm] int id, [FromForm] AppointmentStatus status, [FromForm] string? returnUrl = null)
    {
        var result = await _bookingService.UpdateStatusAsync(id, status);
        if (result)
        {
            TempData["SuccessMessage"] = "Статус записи успешно обновлен.";
        }
        else
        {
            TempData["ErrorMessage"] = "Не удалось обновить статус записи.";
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Timeline");
    }

    // Быстрое ручное добавление записи администратором (например, звонок клиента)
    [HttpPost("quick-create")]
    public async Task<IActionResult> QuickCreate(
        [FromForm] int barberId,
        [FromForm] int serviceId,
        [FromForm] DateTime date,
        [FromForm] string timeSlot,
        [FromForm] string clientName,
        [FromForm] string clientPhone,
        [FromForm] string? notes = null)
    {
        try
        {
            var req = new BookingRequestDto
            {
                BarberId = barberId,
                ServiceId = serviceId,
                Date = date,
                TimeSlot = timeSlot,
                ClientName = clientName,
                ClientPhone = clientPhone,
                Notes = notes
            };

            var app = await _bookingService.CreateAppointmentAsync(req);
            TempData["SuccessMessage"] = $"Запись {app.BookingCode} для клиента {clientName} успешно создана!";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction("Timeline", new { date = date.ToString("yyyy-MM-dd") });
    }

    // Список мастеров
    [HttpGet("barbers")]
    public async Task<IActionResult> Barbers()
    {
        var barbers = await _bookingService.GetActiveBarbersAsync();
        return View(barbers);
    }

    // Услуги и прайс-лист
    [HttpGet("services")]
    public async Task<IActionResult> Services()
    {
        var services = await _bookingService.GetActiveServicesAsync();
        return View(services);
    }
}
