using Booking.Service.Application.Contracts.Persistence;
using MediatR;

namespace Booking.Service.Application.Bookings.Queries.GetBookingSagaState;

/// <summary>
/// Admin/ops read of the booking-payment saga snapshot. Surfaces every column ops needs to triage
/// a saga that drifted into <c>RefundFailed</c> or otherwise looks stuck — the F5 admin endpoint
/// is the consumer.
/// </summary>
public sealed record GetBookingSagaStateQuery(Guid BookingId) : IRequest<BookingSagaStateSnapshot?>;

public sealed class GetBookingSagaStateQueryHandler(IBookingSagaStateReader reader)
    : IRequestHandler<GetBookingSagaStateQuery, BookingSagaStateSnapshot?>
{
    public Task<BookingSagaStateSnapshot?> Handle(GetBookingSagaStateQuery request, CancellationToken cancellationToken)
    {
        return reader.GetByBookingIdAsync(request.BookingId, cancellationToken);
    }
}
