using System.Diagnostics;
using BarberBooking.Web.Models;
using BarberBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace BarberBooking.Web.Controllers;

public class HomeController : Controller
{
    private readonly IBookingService _bookingService;
    private readonly ShopClock _clock;

    public HomeController(IBookingService bookingService, ShopClock clock)
    {
        _bookingService = bookingService;
        _clock = clock;
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.Shop = await _clock.SettingsAsync();
        ViewBag.Barbers = await _bookingService.GetActiveBarbersAsync();
        ViewBag.Services = await _bookingService.GetActiveServicesAsync();
        return View();
    }

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
