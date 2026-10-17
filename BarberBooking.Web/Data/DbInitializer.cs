using BarberBooking.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BarberBooking.Web.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider provider, BootstrapModel? bootstrap)
    {
        var db = provider.GetRequiredService<BarberDbContext>();
        await db.Database.MigrateAsync();
        if (bootstrap is null) return;
        var users = provider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.GetUsersInRoleAsync(Roles.Owner) is { Count: > 0 })
            throw new InvalidOperationException("An owner already exists. Initialization never resets existing accounts.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(bootstrap.UserName, "^[a-zA-Z0-9._-]{3,40}$")
            || string.IsNullOrWhiteSpace(bootstrap.DisplayName) || bootstrap.DisplayName.Length > 100
            || bootstrap.Password.Length is < 12 or > 128 || string.IsNullOrWhiteSpace(bootstrap.ShopName) || bootstrap.ShopName.Length > 100)
            throw new InvalidOperationException("Check the owner login, name, shop name, and password (12–128 characters).");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { Roles.Owner, Roles.Reception, Roles.Barber, Roles.Customer })
            if (!await roleManager.RoleExistsAsync(role)) Ensure(await roleManager.CreateAsync(new IdentityRole(role)));
        var owner = new ApplicationUser { UserName = bootstrap.UserName, DisplayName = bootstrap.DisplayName };
        Ensure(await users.CreateAsync(owner, bootstrap.Password));
        Ensure(await users.AddToRoleAsync(owner, Roles.Owner));
        if (!await db.ShopSettings.AnyAsync()) db.ShopSettings.Add(new ShopSettings { Name = bootstrap.ShopName, DataKeyId = provider.GetRequiredService<Bladez.Core.DataVault>().Fingerprint("identity", "DataKey") });
        if (bootstrap.SeedCatalog && !await db.Barbers.AnyAsync())
        {
            db.Barbers.AddRange(
                new Barber { FullName = "Азамат", Title = "Мастер стрижек", Bio = "Пример карточки мастера. Замените данными своей команды.", BadgeColor = "#86a9ff", AvatarUrl = "/images/barber.svg", WorkingDays = "1,2,3,4,5,6", Rating = 0 },
                new Barber { FullName = "Эмиль", Title = "Мастер бороды", Bio = "Пример карточки мастера. Замените данными своей команды.", BadgeColor = "#bba3ef", AvatarUrl = "/images/barber.svg", WorkingDays = "1,2,3,4,5,6", Rating = 0 });
            db.Services.AddRange(
                new ServiceItem { Name = "Мужская стрижка", Description = "Консультация, стрижка и укладка. Пример услуги — настройте свой прайс.", Price = 800, DurationMinutes = 30 },
                new ServiceItem { Name = "Стрижка и борода", Description = "Комплексный уход за образом. Пример услуги.", Price = 1400, DurationMinutes = 60, Category = "Комплексы" },
                new ServiceItem { Name = "Оформление бороды", Description = "Форма, контур и уход. Пример услуги.", Price = 600, DurationMinutes = 30, Category = "Борода" });
        }
        db.AuditEntries.Add(new AuditEntry { ActorId = owner.Id, Action = "host.initialized", Target = "shop" });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}
