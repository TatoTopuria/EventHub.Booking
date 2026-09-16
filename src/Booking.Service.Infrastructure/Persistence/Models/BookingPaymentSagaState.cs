using MassTransit;

namespace Booking.Service.Infrastructure.Persistence.Models;

public sealed class BookingPaymentSagaState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }

    public string CurrentState { get; set; } = string.Empty;

    public Guid BookingId { get; set; }

    public Guid EventId { get; set; }

    public Guid CustomerId { get; set; }

    public string SeatNumber { get; set; } = string.Empty;

    public decimal ChargeAmount { get; set; }

    public string ChargeCurrency { get; set; } = string.Empty;

    public string ChargeProvider { get; set; } = string.Empty;

    public string ChargeIdempotencyKey { get; set; } = string.Empty;

    public string ChargeMetadataJson { get; set; } = "{}";

    public Guid? PaymentIntentId { get; set; }

    public string? PaymentStatus { get; set; }

    public string? FailureReason { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Outcome of the post-confirmation notification step ("Succeeded", "Failed", or null).</summary>
    public string? NotificationStatus { get; set; }

    public string? NotificationFailureReason { get; set; }

    public DateTime? NotificationCompletedAtUtc { get; set; }

    public string? RefundStatus { get; set; }

    public string? RefundReference { get; set; }

    public DateTime? RefundCompletedAtUtc { get; set; }

    /// <summary>
    /// Wall-clock when the saga entered the <c>Refunding</c> state. Used by
    /// <c>BookingSagaTimeoutWorker</c> to find sagas that have been waiting too long for
    /// <c>RefundCompletedV1</c> from Payment.Service.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="UpdatedAtUtc"/> because <c>UpdatedAtUtc</c> would get bumped by
    /// any later event in <c>Refunding</c> (none today, but adding one would silently disarm the
    /// timeout). Capture the entry time explicitly so the worker's query stays stable.
    /// </remarks>
    public DateTime? RefundRequestedAtUtc { get; set; }

    /// <summary>
    /// Populated by the state machine when <c>RefundTimeoutExpiredV1</c> fires. Lets an operator
    /// querying <c>GET /api/bookings/{id}/saga-state</c> see how long the refund hung before the
    /// timeout escalated.
    /// </summary>
    public int? RefundTimeoutElapsedSeconds { get; set; }
}