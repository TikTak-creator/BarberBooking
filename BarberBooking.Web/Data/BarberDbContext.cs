using BarberBooking.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberBooking.Web.Data;

public class BarberDbContext : DbContext
{
    public BarberDbContext(DbContextOptions<BarberDbContext> options) : base(options)
    {
    }

    public DbSet<Barber> Barbers => Set<Barber>();
    public DbSet<ServiceItem> Services => Set<ServiceItem>();
    public DbSet<Appointment> Appointments => Set<Appointment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Индекс для ускорения выборки слотов и контроля пересечений времени
        modelBuilder.Entity<Appointment>(entity =>
        {
            entity.HasIndex(a => new { a.BarberId, a.AppointmentDate, a.Status })
                  .HasDatabaseName("ix_appointment_barber_date");

            entity.HasIndex(a => a.BookingCode)
                  .IsUnique()
                  .HasDatabaseName("ix_appointment_booking_code");

            entity.HasOne(a => a.Barber)
                  .WithMany(b => b.Appointments)
                  .HasForeignKey(a => a.BarberId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(a => a.Service)
                  .WithMany(s => s.Appointments)
                  .HasForeignKey(a => a.ServiceId)
                  .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
