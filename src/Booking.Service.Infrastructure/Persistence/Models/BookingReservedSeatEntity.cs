namespace Booking.Service.Infrastructure.Persistence.Models;

public sealed class BookingReservedSeatEntity
{
    public Guid BookingId { get; set; }

    public string SeatNumber { get; set; } = string.Empty;

    public BookingEntity Booking { get; set; } = null!;
}
