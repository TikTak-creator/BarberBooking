using BarberBooking.Web.Models;
using Bladez.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BarberBooking.Web.Data;

public class BarberDbContext(DbContextOptions<BarberDbContext> options, DataVault vault) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Barber> Barbers => Set<Barber>();
    public DbSet<ServiceItem> Services => Set<ServiceItem>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<ShopSettings> ShopSettings => Set<ShopSettings>();
    public DbSet<TimeOff> TimeOff => Set<TimeOff>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    private ValueConverter<string, string> Encrypted(string purpose) => new(v => vault.Encrypt(v, purpose), v => vault.Decrypt(v, purpose));
    private ValueConverter<string?, string?> EncryptedOptional(string purpose) => new(v => v == null ? null : vault.Encrypt(v, purpose), v => v == null ? null : vault.Decrypt(v, purpose));

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>(e =>
        {
            e.Property(u => u.DisplayName).HasConversion(Encrypted("User.DisplayName")).HasColumnType("longtext");
            e.Property(u => u.Email).HasConversion(EncryptedOptional("User.Email")).HasColumnType("longtext");
            e.Property(u => u.PhoneNumber).HasConversion(EncryptedOptional("User.Phone")).HasColumnType("longtext");
            e.Property(u => u.NormalizedEmail).HasMaxLength(64);
            e.HasIndex(u => u.NormalizedEmail).HasDatabaseName("EmailIndex").IsUnique();
            e.HasOne(u => u.Barber).WithMany().HasForeignKey(u => u.BarberId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<IdentityUserToken<string>>().Property(t => t.Value).HasConversion(EncryptedOptional("Identity.Token")).HasColumnType("longtext");
        builder.Entity<Appointment>(e =>
        {
            e.Property(a => a.ClientName).HasConversion(Encrypted("Appointment.Name")).HasColumnType("longtext");
            e.Property(a => a.ClientPhone).HasConversion(Encrypted("Appointment.Phone")).HasColumnType("longtext");
            e.Property(a => a.ClientEmail).HasConversion(EncryptedOptional("Appointment.Email")).HasColumnType("longtext");
            e.Property(a => a.Notes).HasConversion(EncryptedOptional("Appointment.Notes")).HasColumnType("longtext");
            e.Property(a => a.CancellationReason).HasConversion(EncryptedOptional("Appointment.Cancellation")).HasColumnType("longtext");
            e.HasIndex(a => new { a.BarberId, a.AppointmentDate, a.Status }).HasDatabaseName("ix_appointment_barber_date");
            e.HasIndex(a => a.BookingCode).IsUnique().HasDatabaseName("ix_appointment_booking_code");
            e.HasIndex(a => a.PhoneFingerprint);
            e.HasOne(a => a.Barber).WithMany(b => b.Appointments).HasForeignKey(a => a.BarberId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.Service).WithMany(s => s.Appointments).HasForeignKey(a => a.ServiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.CustomerUser).WithMany().HasForeignKey(a => a.CustomerUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<TimeOff>(e =>
        {
            e.HasIndex(t => new { t.BarberId, t.Date });
            e.HasOne(t => t.Barber).WithMany().HasForeignKey(t => t.BarberId).OnDelete(DeleteBehavior.Restrict);
            e.Property(t => t.Reason).HasConversion(Encrypted("TimeOff.Reason")).HasColumnType("longtext");
        });
        builder.Entity<AuditEntry>().HasIndex(a => a.AtUtc);
    }
}
