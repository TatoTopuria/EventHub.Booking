using FluentValidation;

namespace Booking.Service.Application.Bookings.Commands.CancelBooking;

public sealed class CancelBookingCommandValidator : AbstractValidator<CancelBookingCommand>
{
    public CancelBookingCommandValidator()
    {
        RuleFor(command => command.BookingId)
            .NotEmpty();
    }
}