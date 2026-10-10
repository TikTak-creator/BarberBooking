using System.Globalization;
using BarberBooking.Web.Data;
using BarberBooking.Web.Services;
using Microsoft.EntityFrameworkCore;

var culture = new CultureInfo("ru-RU");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

var builder = WebApplication.CreateBuilder(args);

// Add services to container
builder.Services.AddControllersWithViews();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Server=localhost;Port=3306;Database=barber_booking_db;User ID=root;Password=NewPassword123!;CharSet=utf8mb4;";

builder.Services.AddDbContext<BarberDbContext>(options =>
{
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 4, 0)));
});

builder.Services.AddScoped<IBookingService, BookingService>();

var app = builder.Build();

// Автоматическая инициализация базы данных и сидирование демо-данных
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<BarberDbContext>();
        await DbInitializer.InitializeAsync(context);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Ошибка инициализации базы данных MySQL.");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
