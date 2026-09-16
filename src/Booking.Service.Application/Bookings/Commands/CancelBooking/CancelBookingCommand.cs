using Booking.Service.Application.Contracts.Persistence;
using Booking.Service.Domain.Enums;
using BuildingBlocks.Primitives;
using MediatR;

namespace Booking.Service.Application.Bookings.Commands.CancelBooking;

public sealed record CancelBookingCommand(Guid BookingId) : IRequest<Result>;

public sealed class CancelBookingCommandHandler(
    IBookingRepository bookingRepository,
    IEventRepository eventRepository,
    IBookingUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<CancelBookingCommand, Result>
{
    public async Task<Result> Handle(CancelBookingCommand request, CancellationToken cancellationToken)
    {
        var booking = await bookingRepository.GetByIdAsync(request.BookingId, cancellationToken);
        if (booking is null)
        {
            return Result.Failure("Booking was not found.");
        }

        if (booking.Status == BookingStatus.Cancelled)
        {
            return Result.Success();
        }

        var eventAggregate = await eventRepository.GetByIdAsync(booking.EventId, cancellationToken);
        if (eventAggregate is null)
        {
            return Result.Failure("Event was not found.");
        }

        foreach (var reservedSeat in booking.ReservedSeats)
        {
            var releaseSeatResult = eventAggregate.MarkSeatReleased(reservedSeat);
            if (releaseSeatResult.IsFailure)
            {
                return releaseSeatResult;
            }
        }

        var cancelResult = booking.Cancel(timeProvider.GetUtcNow().UtcDateTime);
        if (cancelResult.IsFailure)
        {
            return cancelResult;
        }

        await eventRepository.UpdateAsync(eventAggregate, cancellationToken);
        await bookingRepository.UpdateAsync(booking, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}