using System.Diagnostics;
using BarberBooking.Web.Models;
using BarberBooking.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace BarberBooking.Web.Controllers;

public class HomeController : Controller
{
    private readonly IBookingService _bookingService;

    public HomeController(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.Barbers = await _bookingService.GetActiveBarbersAsync();
        ViewBag.Services = await _bookingService.GetActiveServicesAsync();
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
