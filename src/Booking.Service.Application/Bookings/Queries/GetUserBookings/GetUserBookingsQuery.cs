using Booking.Service.Application.Contracts.Persistence;
using Booking.Service.Domain.ValueObjects;
using MediatR;

namespace Booking.Service.Application.Bookings.Queries.GetUserBookings;

public sealed record UserBookingItemResponse(
    Guid BookingId,
    Guid EventId,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? ConfirmedAtUtc,
    IReadOnlyCollection<string> Seats);

public sealed record UserBookingsResponse(
    IReadOnlyCollection<UserBookingItemResponse> Items,
    DateTime? NextLastCreatedAtUtc,
    Guid? NextLastBookingId);

public sealed record GetUserBookingsQuery(
    Guid CustomerId,
    DateTime? LastCreatedAtUtc,
    Guid? LastBookingId,
    int PageSize = 20) : IRequest<UserBookingsResponse>;

public sealed class GetUserBookingsQueryHandler(IBookingRepository bookingRepository)
    : IRequestHandler<GetUserBookingsQuery, UserBookingsResponse>
{
    public async Task<UserBookingsResponse> Handle(GetUserBookingsQuery request, CancellationToken cancellationToken)
    {
        var customerIdResult = CustomerId.Create(request.CustomerId);
        if (customerIdResult.IsFailure)
        {
            return new UserBookingsResponse([], null, null);
        }

        var bookings = await bookingRepository.GetByCustomerIdAsync(
            customerIdResult.Value,
            request.LastCreatedAtUtc,
            request.LastBookingId,
            request.PageSize,
            cancellationToken);

        var items = bookings.Select(booking => new UserBookingItemResponse(
            booking.Id,
            booking.EventId.Value,
            booking.Status.ToString(),
            booking.CreatedAtUtc,
            booking.ConfirmedAtUtc,
            booking.ReservedSeats.Select(seat => seat.Value).ToArray())).ToArray();

        var last = bookings.LastOrDefault();

        return new UserBookingsResponse(items, last?.CreatedAtUtc, last?.Id);
    }
}
