namespace Booking.Service.Application.Contracts.Persistence;

/// <summary>
/// Read-only access to the booking-payment saga's persisted snapshot. The saga itself is owned
/// by MassTransit; this contract is the read side an operator-facing query handler uses to expose
/// the snapshot without touching MassTransit types in the Application layer.
/// </summary>
public interface IBookingSagaStateReader
{
    Task<BookingSagaStateSnapshot?> GetByBookingIdAsync(Guid bookingId, CancellationToken cancellationToken);
}

/// <summary>
/// Application-layer DTO mirroring the columns of <c>booking_payment_saga_states</c>. Lives here
/// so the Application contract does not leak the EF entity into the Api / handler layer.
/// </summary>
public sealed record BookingSagaStateSnapshot(
    Guid CorrelationId,
    Guid BookingId,
    Guid EventId,
    Guid CustomerId,
    string CurrentState,
    string SeatNumber,
    decimal ChargeAmount,
    string ChargeCurrency,
    string ChargeProvider,
    Guid? PaymentIntentId,
    string? PaymentStatus,
    string? FailureReason,
    string? NotificationStatus,
    string? NotificationFailureReason,
    DateTime? NotificationCompletedAtUtc,
    string? RefundStatus,
    string? RefundReference,
    DateTime? RefundRequestedAtUtc,
    DateTime? RefundCompletedAtUtc,
    int? RefundTimeoutElapsedSeconds,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
