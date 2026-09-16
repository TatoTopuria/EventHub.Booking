using Booking.Service.Application.Contracts.Persistence;
using MediatR;

namespace Booking.Service.Application.Events.Queries.ListEvents;

public sealed record EventListItemResponse(
    Guid EventId,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    string Organizer,
    string Status,
    int SeatCount,
    int ReservedSeatCount);

public sealed record ListEventsResponse(
    IReadOnlyCollection<EventListItemResponse> Items,
    DateTime? NextLastStartsAtUtc,
    Guid? NextLastEventId);

public sealed record ListEventsQuery(
    DateTime? LastStartsAtUtc,
    Guid? LastEventId,
    int PageSize = 20) : IRequest<ListEventsResponse>;

public sealed class ListEventsQueryHandler(IEventRepository eventRepository)
    : IRequestHandler<ListEventsQuery, ListEventsResponse>
{
    public async Task<ListEventsResponse> Handle(ListEventsQuery request, CancellationToken cancellationToken)
    {
        var items = await eventRepository.ListAsync(request.LastStartsAtUtc, request.LastEventId, request.PageSize, cancellationToken);

        var responseItems = items
            .Select(eventAggregate => new EventListItemResponse(
                eventAggregate.Id.Value,
                eventAggregate.Schedule.StartsAtUtc,
                eventAggregate.Schedule.EndsAtUtc,
                eventAggregate.Organizer,
                eventAggregate.Status.ToString(),
                eventAggregate.Seats.Count,
                eventAggregate.ReservedSeats.Count))
            .ToArray();

        var last = items.LastOrDefault();

        return new ListEventsResponse(
            responseItems,
            last?.Schedule.StartsAtUtc,
            last?.Id.Value);
    }
}
