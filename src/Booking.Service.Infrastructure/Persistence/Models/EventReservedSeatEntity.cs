namespace Booking.Service.Infrastructure.Persistence.Models;

public sealed class EventReservedSeatEntity
{
    public Guid EventId { get; set; }

    public string SeatNumber { get; set; } = string.Empty;

    public EventEntity Event { get; set; } = null!;
}
