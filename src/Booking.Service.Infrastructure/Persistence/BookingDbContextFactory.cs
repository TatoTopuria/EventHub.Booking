using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Booking.Service.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by the dotnet-ef tool so migration generation does not require the host
/// configuration (which mandates GatewayAuth + JwtSettings + a half-dozen other secrets).
/// </summary>
public sealed class BookingDbContextFactory : IDesignTimeDbContextFactory<BookingDbContext>
{
    private const string DesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=booking_service;Username=booking_user;Password=booking_pass";

    public BookingDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("BOOKING_DB_CONNECTION_STRING")
            ?? DesignTimeConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<BookingDbContext>()
            .UseNpgsql(connectionString);

        return new BookingDbContext(optionsBuilder.Options);
    }
}
