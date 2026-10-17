using Bladez.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BarberBooking.Web.Data;
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<BarberDbContext>
{
    public BarberDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BarberDbContext>().UseMySQL("Server=127.0.0.1;Port=33389;Database=bladez_design;User ID=design;Password=not-a-runtime-credential").Options;
        return new BarberDbContext(options, new DataVault(Path.Combine(Path.GetTempPath(), "Bladez-DesignTime")));
    }
}
