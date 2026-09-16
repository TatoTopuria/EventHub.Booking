using Booking.Service.Application.Abstractions;
using Booking.Service.Application.Bookings.Concurrency;
using Booking.Service.Application.Bookings.Validation;
using Booking.Service.Application.Contracts.Persistence;
using Booking.Service.Domain.ValueObjects;
using BuildingBlocks.Primitives;
using MediatR;

using BookingAggregate = Booking.Service.Domain.Aggregates.Booking;

namespace Booking.Service.Application.Bookings.Commands.ReserveSeat;

public sealed record ReserveSeatCommand(Guid EventId, Guid CustomerId, string SeatNumber) : IRequest<Result<Guid>>, IMultiAggregateCommand;

/// <summary>
/// Stable error message returned when the per-seat distributed lock could not be acquired.
/// Integration tests and 409 mapping rely on this exact string; do not change it lightly.
/// </summary>
internal static class ReserveSeatErrors
{
    public const string SeatLockUnavailable = "Seat is currently being reserved by another request. Please try again.";
}

public sealed class ReserveSeatCommandHandler(
    IEventRepository eventRepository,
    IBookingRepository bookingRepository,
    IBookingUnitOfWork unitOfWork,
    IBookingValidationPipeline validationPipeline,
    ISeatReservationLock seatReservationLock,
    TimeProvider timeProvider)
    : IRequestHandler<ReserveSeatCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(ReserveSeatCommand request, CancellationToken cancellationToken)
    {
        var eventIdResult = EventId.Create(request.EventId);
        if (eventIdResult.IsFailure)
        {
            return Result<Guid>.Failure(eventIdResult.Error);
        }

        var customerIdResult = CustomerId.Create(request.CustomerId);
        if (customerIdResult.IsFailure)
        {
            return Result<Guid>.Failure(customerIdResult.Error);
        }

        var seatResult = SeatNumber.Create(request.SeatNumber);
        if (seatResult.IsFailure)
        {
            return Result<Guid>.Failure(seatResult.Error);
        }

        // Acquire the per-seat distributed lock BEFORE loading the event aggregate. Two reservers
        // racing for the same seat block here; the loser sees a clean "try again" instead of getting
        // through validation and then losing on the DB concurrency token (which still catches edge
        // cases — see F6 DECISIONS for the defense-in-depth rationale).
        await using var seatLock = await seatReservationLock.TryAcquireAsync(
            eventIdResult.Value,
            seatResult.Value,
            cancellationToken);

        if (seatLock is null)
        {
            return Result<Guid>.Failure(ReserveSeatErrors.SeatLockUnavailable);
        }

        var eventAggregate = await eventRepository.GetByIdAsync(eventIdResult.Value, cancellationToken);
        if (eventAggregate is null)
        {
            return Result<Guid>.Failure("Event was not found.");
        }

        // Chain of Responsibility runs business-rule checks before any aggregate mutation.
        // The aggregate is still the source of truth for the invariant; the chain produces
        // friendlier error messages and short-circuits before we touch state.
        var validationResult = await validationPipeline.ValidateAsync(
            new BookingValidationContext(eventAggregate, customerIdResult.Value, seatResult.Value),
            cancellationToken);

        if (validationResult.IsFailure)
        {
            return Result<Guid>.Failure(validationResult.Error);
        }

        var markSeatResult = eventAggregate.MarkSeatReserved(seatResult.Value);
        if (markSeatResult.IsFailure)
        {
            return Result<Guid>.Failure(markSeatResult.Error);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var bookingId = Guid.NewGuid();
        var bookingCreateResult = BookingAggregate.Create(bookingId, eventIdResult.Value, customerIdResult.Value, now);
        if (bookingCreateResult.IsFailure)
        {
            return Result<Guid>.Failure(bookingCreateResult.Error);
        }

        var reserveSeatResult = bookingCreateResult.Value.ReserveSeat(seatResult.Value, now);
        if (reserveSeatResult.IsFailure)
        {
            return Result<Guid>.Failure(reserveSeatResult.Error);
        }

        await bookingRepository.AddAsync(bookingCreateResult.Value, cancellationToken);
        await eventRepository.UpdateAsync(eventAggregate, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(bookingId);
    }
}
