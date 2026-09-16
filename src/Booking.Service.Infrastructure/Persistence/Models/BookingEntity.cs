namespace Booking.Service.Infrastructure.Persistence.Models;

public sealed class BookingEntity
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }

    public Guid CustomerId { get; set; }

    public int Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ConfirmedAtUtc { get; set; }

    public bool IsPaid { get; set; }

    public decimal? PaymentAmount { get; set; }

    public string? PaymentCurrency { get; set; }

    public ICollection<BookingReservedSeatEntity> ReservedSeats { get; set; } = [];
}
