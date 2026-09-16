using Booking.Service.Application.Contracts.Persistence;
using Booking.Service.Domain.ValueObjects;
using MediatR;

namespace Booking.Service.Application.Events.Queries.GetEventById;

public sealed record EventDetailsResponse(
    Guid EventId,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    string Organizer,
    string Status,
    IReadOnlyCollection<string> Seats,
    IReadOnlyCollection<string> ReservedSeats);

public sealed record GetEventByIdQuery(Guid EventId) : IRequest<EventDetailsResponse?>;

public sealed class GetEventByIdQueryHandler(IEventRepository eventRepository)
    : IRequestHandler<GetEventByIdQuery, EventDetailsResponse?>
{
    public async Task<EventDetailsResponse?> Handle(GetEventByIdQuery request, CancellationToken cancellationToken)
    {
        var eventIdResult = EventId.Create(request.EventId);
        if (eventIdResult.IsFailure)
        {
            return null;
        }

        var eventAggregate = await eventRepository.GetByIdAsync(eventIdResult.Value, cancellationToken);
        if (eventAggregate is null)
        {
            return null;
        }

        return new EventDetailsResponse(
            eventAggregate.Id.Value,
            eventAggregate.Schedule.StartsAtUtc,
            eventAggregate.Schedule.EndsAtUtc,
            eventAggregate.Organizer,
            eventAggregate.Status.ToString(),
            eventAggregate.Seats.Select(seat => seat.Value).ToArray(),
            eventAggregate.ReservedSeats.Select(seat => seat.Value).ToArray());
    }
}
