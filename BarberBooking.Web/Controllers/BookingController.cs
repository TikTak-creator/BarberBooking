using System.Globalization;
using BarberBooking.Web.Models;
using BarberBooking.Web.Services;
using Bladez.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BarberBooking.Web.Controllers;

public sealed class BookingController(IBookingService bookings, BookingAccess access, ShopClock clock, Actor actor) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int? serviceId, int? barberId)
    {
        var settings = await clock.SettingsAsync();
        if (actor.Id is null && !settings.AllowGuestBooking) return RedirectToAction("Login", "Account", new { returnUrl = Url.Action(nameof(Index), new { serviceId, barberId }) });
        ViewBag.Services = await bookings.GetActiveServicesAsync(); ViewBag.Barbers = await bookings.GetActiveBarbersAsync();
        ViewBag.SelectedServiceId = serviceId; ViewBag.SelectedBarberId = barberId;
        ViewBag.Shop = settings; ViewBag.Today = clock.Now(settings).Date;
        return View();
    }
    [HttpGet("/api/booking/slots"), EnableRateLimiting("slots")]
    public async Task<IActionResult> GetSlots(int barberId, int serviceId, string date)
    {
        if (barberId <= 0 || serviceId <= 0 || !DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return BadRequest(new { success = false, message = "Проверьте параметры запроса." });
        try { var slots = await bookings.GetAvailableSlotsAsync(barberId, serviceId, parsed); return Ok(new { success = true, slots = slots.Select(s => new { time = s.FormattedTime, s.IsAvailable, s.Reason }) }); }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
    }
    [HttpPost("/api/booking/create"), EnableRateLimiting("booking")]
    public async Task<IActionResult> Create([FromBody] BookingRequestDto request)
    {
        if (!ModelState.IsValid) return BadRequest(new { success = false, message = "Проверьте поля формы записи." });
        try
        {
            var key = actor.Id is null ? DataVault.RandomSecret() : null;
            var appointment = await bookings.CreateAppointmentAsync(request, key);
            if (key is not null) { access.Grant(appointment, key); TempData["GuestKey"] = key; }
            return Ok(new { success = true, bookingCode = appointment.BookingCode, redirectUrl = Url.Action(nameof(Confirmation), new { code = appointment.BookingCode }) });
        }
        catch (UnauthorizedAccessException) { return Unauthorized(new { success = false, message = "Войдите в аккаунт для записи." }); }
        catch (DoubleBookingConflictException ex) { return Conflict(new { success = false, message = ex.Message }); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return BadRequest(new { success = false, message = ex.Message }); }
    }
    [HttpGet]
    public async Task<IActionResult> Confirmation(string code)
    {
        var row = await bookings.GetAppointmentByCodeAsync(code);
        if (row is null || !access.CanRead(row)) return NotFound();
        ViewBag.GuestKey = TempData["GuestKey"];
        ViewBag.CanCancel = row.Status is AppointmentStatus.Pending or AppointmentStatus.Confirmed
            && row.AppointmentDate.Add(row.StartTime) >= (await clock.NowAsync()).AddMinutes((await clock.SettingsAsync()).CancellationLeadMinutes);
        return View(new BookingConfirmationViewModel { BookingCode = row.BookingCode, Version = row.Version, ClientName = row.ClientName,
            ClientPhone = row.ClientPhone, BarberName = row.Barber.FullName, ServiceName = row.ServiceNameSnapshot, Price = row.Price,
            Date = row.AppointmentDate, StartTime = row.StartTime, EndTime = row.EndTime, Status = row.Status });
    }
    // A phone number or booking locator alone never authorizes access.
    [HttpGet] public IActionResult Track() => View();
    [HttpPost, EnableRateLimiting("auth")]
    public async Task<IActionResult> Track(string code, string key)
    {
        var row = await bookings.GetAppointmentByCodeAsync(code ?? "");
        if (row is not null && (access.CanRead(row) || key is not null && access.Grant(row, key)))
            return RedirectToAction(nameof(Confirmation), new { code = row.BookingCode });
        ModelState.AddModelError("", "Запись недоступна. Проверьте код и секретный ключ или войдите в свой аккаунт.");
        return View();
    }
    [HttpPost, EnableRateLimiting("booking")]
    public async Task<IActionResult> Cancel(string code, string? reason, Guid version)
    {
        var result = await bookings.CancelByClientAsync(code ?? "", reason ?? "Отменено клиентом", version);
        TempData[result ? "SuccessMessage" : "ErrorMessage"] = result ? "Запись отменена." : "Отмена недоступна: проверьте доступ, состояние и срок отмены. Обновите страницу.";
        return RedirectToAction(nameof(Confirmation), new { code });
    }
}
