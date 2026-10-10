using BarberBooking.Web.Models;
using BarberBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace BarberBooking.Web.Controllers;

public class BookingController : Controller
{
    private readonly IBookingService _bookingService;
    private readonly ILogger<BookingController> _logger;

    public BookingController(IBookingService bookingService, ILogger<BookingController> logger)
    {
        _bookingService = bookingService;
        _logger = logger;
    }

    // Главная страница пошагового виджета бронирования
    public async Task<IActionResult> Index(int? serviceId = null, int? barberId = null)
    {
        ViewBag.Services = await _bookingService.GetActiveServicesAsync();
        ViewBag.Barbers = await _bookingService.GetActiveBarbersAsync();
        ViewBag.SelectedServiceId = serviceId;
        ViewBag.SelectedBarberId = barberId;
        return View();
    }

    // REST API для получения доступных слотов времени
    [HttpGet]
    [Route("api/booking/slots")]
    public async Task<IActionResult> GetSlots([FromQuery] int barberId, [FromQuery] int serviceId, [FromQuery] string date)
    {
        if (barberId <= 0 || serviceId <= 0 || !DateTime.TryParse(date, out var parsedDate))
        {
            return BadRequest(new { success = false, message = "Некорректные параметры запроса." });
        }

        try
        {
            var slots = await _bookingService.GetAvailableSlotsAsync(barberId, serviceId, parsedDate);
            return Ok(new
            {
                success = true,
                date = parsedDate.ToString("yyyy-MM-dd"),
                slots = slots.Select(s => new
                {
                    time = s.FormattedTime,
                    isAvailable = s.IsAvailable,
                    reason = s.Reason
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка получения слотов для мастера {BarberId}", barberId);
            return StatusCode(500, new { success = false, message = "Ошибка при загрузке слотов." });
        }
    }

    // Создание бронирования через AJAX или форму
    [HttpPost]
    [Route("api/booking/create")]
    public async Task<IActionResult> Create([FromBody] BookingRequestDto request)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            return BadRequest(new { success = false, message = string.Join("; ", errors) });
        }

        try
        {
            var appointment = await _bookingService.CreateAppointmentAsync(request);
            return Ok(new
            {
                success = true,
                bookingCode = appointment.BookingCode,
                clientName = appointment.ClientName,
                barberName = appointment.Barber.FullName,
                serviceName = appointment.Service.Name,
                date = appointment.AppointmentDate.ToString("dd.MM.yyyy"),
                time = appointment.StartTime.ToString(@"hh\:mm"),
                price = appointment.Price,
                redirectUrl = Url.Action("Confirmation", "Booking", new { code = appointment.BookingCode })
            });
        }
        catch (DoubleBookingConflictException ex)
        {
            // Ошибка пересечения / двойного бронирования
            return Conflict(new { success = false, message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при сохранении бронирования");
            return StatusCode(500, new { success = false, message = "Произошла системная ошибка при сохранении записи. Пожалуйста, попробуйте снова." });
        }
    }

    // Страница подтверждения записи
    public async Task<IActionResult> Confirmation(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return RedirectToAction("Index");

        var app = await _bookingService.GetAppointmentByCodeAsync(code);
        if (app == null)
            return NotFound("Запись с указанным кодом не найдена.");

        var model = new BookingConfirmationViewModel
        {
            BookingCode = app.BookingCode,
            ClientName = app.ClientName,
            ClientPhone = app.ClientPhone,
            BarberName = app.Barber.FullName,
            ServiceName = app.Service.Name,
            Price = app.Price,
            Date = app.AppointmentDate,
            StartTime = app.StartTime,
            EndTime = app.EndTime,
            Status = app.Status
        };

        return View(model);
    }

    // Проверка и отслеживание статуса записи клиентом
    public async Task<IActionResult> Track(string? code, string? phone)
    {
        List<Appointment> results = new();

        if (!string.IsNullOrWhiteSpace(code))
        {
            var app = await _bookingService.GetAppointmentByCodeAsync(code);
            if (app != null) results.Add(app);
        }
        else if (!string.IsNullOrWhiteSpace(phone))
        {
            results = await _bookingService.GetAppointmentsByPhoneAsync(phone);
        }

        ViewBag.SearchCode = code;
        ViewBag.SearchPhone = phone;
        return View(results);
    }

    // Отмена записи клиентом
    [HttpPost]
    public async Task<IActionResult> Cancel(string code, string reason)
    {
        if (string.IsNullOrWhiteSpace(code))
            return BadRequest();

        var success = await _bookingService.CancelByClientAsync(code, string.IsNullOrWhiteSpace(reason) ? "Отменено клиентом" : reason);
        if (success)
        {
            TempData["SuccessMessage"] = $"Запись {code} успешно отменена.";
        }
        else
        {
            TempData["ErrorMessage"] = "Не удалось отменить запись (возможно, она уже отменена или не существует).";
        }

        return RedirectToAction("Track", new { code });
    }
}
