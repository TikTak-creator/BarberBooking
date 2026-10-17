using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.RateLimiting;
using BarberBooking.Web.Data;
using BarberBooking.Web.Models;
using BarberBooking.Web.Services;
using Bladez.Core;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("ru-RU");
CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("ru-RU");
var builder = WebApplication.CreateBuilder(args);
var dataPath = HostSettings.DataDirectory;
var host = HostSettings.Load(dataPath);
host.ListenUrl = Environment.GetEnvironmentVariable("BLADEZ_LISTEN_URL") ?? host.ListenUrl;
host.PublicUrl = Environment.GetEnvironmentVariable("BLADEZ_PUBLIC_URL") ?? host.PublicUrl;
host.TrustedProxy = Environment.GetEnvironmentVariable("BLADEZ_TRUSTED_PROXY") ?? host.TrustedProxy;
var vault = new DataVault(dataPath);
var connectionFile = Environment.GetEnvironmentVariable("BLADEZ_CONNECTION_FILE");
var connection = !string.IsNullOrWhiteSpace(connectionFile) ? File.ReadAllText(connectionFile).Trim()
    : builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connection) && File.Exists(Path.Combine(dataPath, "secrets.vault"))) connection = vault.ReadSecrets().ConnectionString;
if (string.IsNullOrWhiteSpace(connection))
    throw new InvalidOperationException("Bladez is not configured. Run the Windows Host setup or Linux installer. No default database password is provided.");
builder.WebHost.UseUrls(host.ListenUrl).ConfigureKestrel(o => { o.AddServerHeader = false; o.Limits.MaxRequestBodySize = 65_536; });
builder.Services.AddSingleton(vault);
builder.Services.AddSingleton(host);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddDbContext<BarberDbContext>(o => o.UseMySQL(connection));
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
{
    o.Password.RequiredLength = 12; o.Password.RequiredUniqueChars = 5;
    o.Password.RequireDigit = true; o.Password.RequireLowercase = true; o.Password.RequireUppercase = true; o.Password.RequireNonAlphanumeric = false;
    o.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._";
    o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15); o.Lockout.AllowedForNewUsers = true;
}).AddEntityFrameworkStores<BarberDbContext>().AddDefaultTokenProviders().AddClaimsPrincipalFactory<UserClaimsFactory>();
builder.Services.AddScoped<ILookupNormalizer, PrivateNormalizer>();
builder.Services.Configure<PasswordHasherOptions>(o => o.IterationCount = 600_000);
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromMinutes(30));
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "Bladez.Session"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.LoginPath = "/Account/Login"; o.AccessDeniedPath = "/Account/Denied";
    o.ExpireTimeSpan = TimeSpan.FromHours(8); o.SlidingExpiration = true;
    o.Events.OnValidatePrincipal = async context =>
    {
        await SecurityStampValidator.ValidatePrincipalAsync(context);
        if (context.Principal?.Identity?.IsAuthenticated == true)
        {
            var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.GetUserAsync(context.Principal);
            if (user is null || !user.IsActive) { context.RejectPrincipal(); await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme); }
        }
    };
    o.Events.OnRedirectToLogin = context => { if (context.Request.Path.StartsWithSegments("/api")) context.Response.StatusCode = 401; else context.Response.Redirect(context.RedirectUri); return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = context => { if (context.Request.Path.StartsWithSegments("/api")) context.Response.StatusCode = 403; else context.Response.Redirect(context.RedirectUri); return Task.CompletedTask; };
});
builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, o => { o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Lax; o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; o.ExpireTimeSpan = TimeSpan.FromMinutes(5); });
var keyPath = Path.Combine(dataPath, "data-protection"); PrivateFiles.Directory(keyPath);
var protection = builder.Services.AddDataProtection().SetApplicationName("Bladez.Business.v1").PersistKeysToFileSystem(new DirectoryInfo(keyPath));
if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.Name = "Bladez.CSRF"; o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; });
builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddScoped<Actor>(); builder.Services.AddScoped<Audit>(); builder.Services.AddScoped<ShopClock>(); builder.Services.AddScoped<BookingAccess>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<AccountMail>();
builder.Services.AddHostFiltering(o => o.AllowedHosts = [new Uri(host.PublicUrl).Host, "localhost", "127.0.0.1", "[::1]"]);
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear(); o.KnownProxies.Clear();
    if (IPAddress.TryParse(host.TrustedProxy, out var proxy)) o.KnownProxies.Add(proxy);
    else o.ForwardedHeaders = ForwardedHeaders.None;
    o.ForwardLimit = 1;
});
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.OnRejected = async (context, cancellation) => { context.HttpContext.Response.Headers.RetryAfter = "300"; await context.HttpContext.Response.WriteAsync("Слишком много запросов. Повторите позже.", cancellation); };
    foreach (var (name, limit, minutes) in new[] { ("auth", builder.Configuration.GetValue("Security:AuthPermitLimit", 10), 5), ("booking", 12, 10), ("slots", 120, 1) })
        o.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter((context.Connection.RemoteIpAddress?.ToString() ?? "unknown") + ":" + name,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(minutes), QueueLimit = 0, AutoReplenishment = true }));
});
var app = builder.Build();
if (args.Contains("--initialize") || args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    BootstrapModel? bootstrap = null;
    if (args.Contains("--initialize"))
    {
        var bootstrapFile = Environment.GetEnvironmentVariable("BLADEZ_BOOTSTRAP_FILE");
        var json = bootstrapFile is null ? await Console.In.ReadToEndAsync() : await File.ReadAllTextAsync(bootstrapFile);
        bootstrap = JsonSerializer.Deserialize<BootstrapModel>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("Owner setup is required.");
    }
    await DbInitializer.InitializeAsync(scope.ServiceProvider, bootstrap);
    Console.WriteLine("BLADEZ_INITIALIZED");
    return;
}
app.UseForwardedHeaders();
app.UseHostFiltering();
app.Use(async (http, next) =>
{
    var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)); http.Items["CspNonce"] = nonce;
    http.Response.Headers.ContentSecurityPolicy = $"default-src 'self'; script-src 'self' 'nonce-{nonce}'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; object-src 'none'; base-uri 'self'; form-action 'self'";
    http.Response.Headers.XContentTypeOptions = "nosniff"; http.Response.Headers.XFrameOptions = "DENY";
    http.Response.Headers["Referrer-Policy"] = "no-referrer"; http.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    http.Response.Headers.CacheControl = "no-cache, no-store";
    http.Response.Headers.Pragma = "no-cache";
    if (http.Request.IsHttps) http.Response.Headers.StrictTransportSecurity = "max-age=31536000";
    var localHttp = host.AllowLoopbackHttp && http.Connection.RemoteIpAddress is not null && IPAddress.IsLoopback(http.Connection.RemoteIpAddress)
        && (http.Request.Host.Host is "127.0.0.1" or "localhost" or "[::1]");
    if (!http.Request.IsHttps && !localHttp) { http.Response.StatusCode = 426; await http.Response.WriteAsync("HTTPS is required. Configure the trusted HTTPS proxy."); return; }
    await next();
});
app.UseExceptionHandler("/Home/Error");
app.UseStaticFiles();
app.Use(async (http, next) =>
{
    if (!http.Request.Path.StartsWithSegments("/health"))
    {
        try
        {
            var db = http.RequestServices.GetRequiredService<BarberDbContext>();
            var keyId = await db.ShopSettings.Where(s => s.Id == 1).Select(s => s.DataKeyId).SingleOrDefaultAsync();
            if (keyId != vault.Fingerprint("identity", "DataKey"))
            { http.Response.StatusCode = 503; await http.Response.WriteAsync("Ключ данных не соответствует базе. Восстановите резервную копию на сервере."); return; }
        }
        catch (Exception error) when (error is MySql.Data.MySqlClient.MySqlException or InvalidOperationException)
        { http.Response.StatusCode = 503; await http.Response.WriteAsync("Сервис временно недоступен. Проверьте состояние сервера."); return; }
    }
    await next();
});
app.UseRouting(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization();
app.Use(async (http, next) =>
{
    if (http.User.Identity?.IsAuthenticated == true && !http.Request.Path.StartsWithSegments("/health")
        && !http.Request.Path.Equals("/Account/ChangePassword", StringComparison.OrdinalIgnoreCase)
        && !http.Request.Path.Equals("/Account/Logout", StringComparison.OrdinalIgnoreCase))
    {
        var user = await http.RequestServices.GetRequiredService<UserManager<ApplicationUser>>().GetUserAsync(http.User);
        if (user?.MustChangePassword == true) { http.Response.Redirect("/Account/ChangePassword"); return; }
    }
    await next();
});
app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (BarberDbContext db) =>
{
    try { return await db.Database.CanConnectAsync() && await db.ShopSettings.AnyAsync(s => s.Id == 1 && s.DataKeyId == vault.Fingerprint("identity", "DataKey")) ? Results.Ok(new { ready = true }) : Results.StatusCode(503); }
    catch { return Results.StatusCode(503); }
});
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
if (OperatingSystem.IsWindows() && File.Exists(Path.Combine(dataPath, "secrets.vault")))
{
    app.MapPost("/internal/stop", async (HttpContext http) =>
    {
        var expected = vault.ReadSecrets().ControlKey;
        var supplied = http.Request.Headers["X-Bladez-Control"].ToString();
        if (http.Connection.RemoteIpAddress is null || !IPAddress.IsLoopback(http.Connection.RemoteIpAddress) || expected.Length < 32
            || supplied.Length > 100 || !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(supplied), System.Text.Encoding.UTF8.GetBytes(expected))) return Results.NotFound();
        await Task.Yield(); app.Lifetime.StopApplication(); return Results.Ok();
    });
}
app.Run();
public partial class Program;
