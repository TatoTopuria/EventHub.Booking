using Booking.Service.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Booking.Service.Infrastructure.Persistence;

/// <summary>
/// EF-backed read of the saga state snapshot. Uses <c>AsNoTracking</c> + the unique
/// <c>BookingId</c> index so the admin endpoint stays a single-row lookup regardless of how big
/// the saga state table grows.
/// </summary>
public sealed class EfBookingSagaStateReader(BookingDbContext dbContext) : IBookingSagaStateReader
{
    public async Task<BookingSagaStateSnapshot?> GetByBookingIdAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.BookingPaymentSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(saga => saga.BookingId == bookingId, cancellationToken);

        if (entity is null)
        {
            return null;
        }

        return new BookingSagaStateSnapshot(
            CorrelationId: entity.CorrelationId,
            BookingId: entity.BookingId,
            EventId: entity.EventId,
            CustomerId: entity.CustomerId,
            CurrentState: entity.CurrentState,
            SeatNumber: entity.SeatNumber,
            ChargeAmount: entity.ChargeAmount,
            ChargeCurrency: entity.ChargeCurrency,
            ChargeProvider: entity.ChargeProvider,
            PaymentIntentId: entity.PaymentIntentId,
            PaymentStatus: entity.PaymentStatus,
            FailureReason: entity.FailureReason,
            NotificationStatus: entity.NotificationStatus,
            NotificationFailureReason: entity.NotificationFailureReason,
            NotificationCompletedAtUtc: entity.NotificationCompletedAtUtc,
            RefundStatus: entity.RefundStatus,
            RefundReference: entity.RefundReference,
            RefundRequestedAtUtc: entity.RefundRequestedAtUtc,
            RefundCompletedAtUtc: entity.RefundCompletedAtUtc,
            RefundTimeoutElapsedSeconds: entity.RefundTimeoutElapsedSeconds,
            CreatedAtUtc: entity.CreatedAtUtc,
            UpdatedAtUtc: entity.UpdatedAtUtc);
    }
}
