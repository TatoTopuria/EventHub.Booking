using FluentValidation;

namespace Booking.Service.Application.Bookings.Commands.ReserveSeat;

public sealed class ReserveSeatCommandValidator : AbstractValidator<ReserveSeatCommand>
{
    public ReserveSeatCommandValidator()
    {
        RuleFor(command => command.EventId)
            .NotEmpty();

        RuleFor(command => command.CustomerId)
            .NotEmpty();

        RuleFor(command => command.SeatNumber)
            .NotEmpty();
    }
}
