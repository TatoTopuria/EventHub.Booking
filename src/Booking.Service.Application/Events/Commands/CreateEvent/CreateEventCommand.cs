using Booking.Service.Application.Contracts.Persistence;
using Booking.Service.Domain.Aggregates;
using Booking.Service.Domain.ValueObjects;
using BuildingBlocks.Primitives;
using MediatR;

namespace Booking.Service.Application.Events.Commands.CreateEvent;

public sealed record CreateEventCommand(
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    string Organizer,
    IReadOnlyCollection<string> Seats) : IRequest<Result<Guid>>;

public sealed class CreateEventCommandHandler(IEventRepository eventRepository, IBookingUnitOfWork unitOfWork)
    : IRequestHandler<CreateEventCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateEventCommand request, CancellationToken cancellationToken)
    {
        var scheduleResult = EventSchedule.Create(request.StartsAtUtc, request.EndsAtUtc);
        if (scheduleResult.IsFailure)
        {
            return Result<Guid>.Failure(scheduleResult.Error);
        }

        var seatResults = request.Seats.Select(SeatNumber.Create).ToList();
        if (seatResults.Any(result => result.IsFailure))
        {
            var invalidSeat = seatResults.First(result => result.IsFailure);
            return Result<Guid>.Failure(invalidSeat.Error);
        }

        var eventId = EventId.New();
        var createResult = Event.Create(
            eventId,
            scheduleResult.Value,
            request.Organizer,
            seatResults.Select(result => result.Value));

        if (createResult.IsFailure)
        {
            return Result<Guid>.Failure(createResult.Error);
        }

        await eventRepository.AddAsync(createResult.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(eventId.Value);
    }
}
