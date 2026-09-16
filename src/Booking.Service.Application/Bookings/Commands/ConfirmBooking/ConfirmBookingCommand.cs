using Booking.Service.Application.Contracts.Persistence;
using Booking.Service.Domain.Enums;
using Booking.Service.Domain.ValueObjects;
using BuildingBlocks.Primitives;
using MediatR;

namespace Booking.Service.Application.Bookings.Commands.ConfirmBooking;

public sealed record ConfirmBookingCommand(Guid BookingId, decimal Amount, string Currency) : IRequest<Result>;

public sealed class ConfirmBookingCommandHandler(
    IBookingRepository bookingRepository,
    IBookingUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<ConfirmBookingCommand, Result>
{
    public async Task<Result> Handle(ConfirmBookingCommand request, CancellationToken cancellationToken)
    {
        var booking = await bookingRepository.GetByIdAsync(request.BookingId, cancellationToken);
        if (booking is null)
        {
            return Result.Failure("Booking was not found.");
        }

        if (booking.Status == BookingStatus.Confirmed)
        {
            return Result.Success();
        }

        var moneyResult = Money.Create(request.Amount, request.Currency);
        if (moneyResult.IsFailure)
        {
            return Result.Failure(moneyResult.Error);
        }

        var registerPaymentResult = booking.RegisterPayment(moneyResult.Value);
        if (registerPaymentResult.IsFailure)
        {
            return registerPaymentResult;
        }

        var confirmResult = booking.Confirm(timeProvider.GetUtcNow().UtcDateTime);
        if (confirmResult.IsFailure)
        {
            return confirmResult;
        }

        await bookingRepository.UpdateAsync(booking, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
