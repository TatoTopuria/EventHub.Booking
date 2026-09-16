namespace Booking.Service.Infrastructure.Persistence.Models;

public sealed class EventSeatEntity
{
    public Guid EventId { get; set; }

    public string SeatNumber { get; set; } = string.Empty;

    /// <summary>
    /// EF Core concurrency token. Configured via <c>IsConcurrencyToken()</c> in
    /// <c>EventSeatEntityConfiguration</c>; bumped explicitly by <c>EventMapper.Apply</c> whenever a
    /// seat transitions to or from the reserved state. Stale UPDATEs trigger
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>, which the
    /// API middleware maps to HTTP 409.
    /// </summary>
    public uint Version { get; set; }

    public EventEntity Event { get; set; } = null!;
}
