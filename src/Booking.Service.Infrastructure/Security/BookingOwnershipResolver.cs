using Booking.Service.Application.Contracts.Persistence;
using BuildingBlocks.Security;

namespace Booking.Service.Infrastructure.Security;

/// <summary>
/// Booking-service implementation of <see cref="IBookingOwnershipResolver"/>. Reads through the
/// same <see cref="IBookingRepository"/> the rest of the service uses — no shadow query path,
/// no second copy of the aggregate's mapping rules. If a booking was deleted between the auth
/// check and the handler running, both see the same "not found" answer.
/// </summary>
public sealed class BookingOwnershipResolver(IBookingRepository bookingRepository) : IBookingOwnershipResolver
{
    public async Task<Guid?> GetOwnerCustomerIdAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await bookingRepository.GetByIdAsync(bookingId, cancellationToken);
        return booking?.CustomerId.Value;
    }
}
