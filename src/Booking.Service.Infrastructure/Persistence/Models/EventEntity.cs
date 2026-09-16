namespace Booking.Service.Infrastructure.Persistence.Models;

public sealed class EventEntity
{
    public Guid Id { get; set; }

    public DateTime StartsAtUtc { get; set; }

    public DateTime EndsAtUtc { get; set; }

    public string Organizer { get; set; } = string.Empty;

    public int Status { get; set; }

    public ICollection<EventSeatEntity> Seats { get; set; } = [];

    public ICollection<EventReservedSeatEntity> ReservedSeats { get; set; } = [];
}
