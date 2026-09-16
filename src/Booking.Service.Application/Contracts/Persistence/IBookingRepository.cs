using Booking.Service.Domain.Aggregates;
using Booking.Service.Domain.ValueObjects;

namespace Booking.Service.Application.Contracts.Persistence;

using BookingAggregate = Booking.Service.Domain.Aggregates.Booking;

public interface IBookingRepository
{
    Task AddAsync(BookingAggregate booking, CancellationToken cancellationToken = default);

    Task UpdateAsync(BookingAggregate booking, CancellationToken cancellationToken = default);

    Task<BookingAggregate?> GetByIdAsync(Guid bookingId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<BookingAggregate>> GetByCustomerIdAsync(
        CustomerId customerId,
        DateTime? lastCreatedAtUtc,
        Guid? lastBookingId,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns up to <paramref name="batchSize"/> bookings still in
    /// <see cref="Booking.Service.Domain.Enums.BookingStatus.PendingPayment"/> whose
    /// <see cref="BookingAggregate.CreatedAtUtc"/> is strictly before <paramref name="cutoffUtc"/>.
    /// Used by the expiry worker.
    /// </summary>
    Task<IReadOnlyCollection<BookingAggregate>> GetExpiringPendingAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default);
}
