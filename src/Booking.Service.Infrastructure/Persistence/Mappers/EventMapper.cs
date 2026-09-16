using Booking.Service.Domain.Aggregates;
using Booking.Service.Domain.Enums;
using Booking.Service.Domain.ValueObjects;
using Booking.Service.Infrastructure.Persistence.Models;

namespace Booking.Service.Infrastructure.Persistence.Mappers;

internal static class EventMapper
{
    public static Event ToDomain(EventEntity entity)
    {
        var schedule = EventSchedule.Create(entity.StartsAtUtc, entity.EndsAtUtc).Value;
        var seats = entity.Seats.Select(seat => SeatNumber.Create(seat.SeatNumber).Value);

        var eventAggregate = Event.Create(new EventId(entity.Id), schedule, entity.Organizer, seats).Value;

        if ((EventStatus)entity.Status is EventStatus.Published or EventStatus.Cancelled)
        {
            eventAggregate.Publish();
        }

        foreach (var seat in entity.ReservedSeats)
        {
            eventAggregate.MarkSeatReserved(SeatNumber.Create(seat.SeatNumber).Value);
        }

        if ((EventStatus)entity.Status == EventStatus.Cancelled)
        {
            eventAggregate.Cancel();
        }

        eventAggregate.ClearDomainEvents();
        return eventAggregate;
    }

    public static EventEntity ToEntity(Event eventAggregate)
    {
        return new EventEntity
        {
            Id = eventAggregate.Id.Value,
            StartsAtUtc = eventAggregate.Schedule.StartsAtUtc,
            EndsAtUtc = eventAggregate.Schedule.EndsAtUtc,
            Organizer = eventAggregate.Organizer,
            Status = (int)eventAggregate.Status,
            Seats = eventAggregate.Seats
                .Select(seat => new EventSeatEntity { EventId = eventAggregate.Id.Value, SeatNumber = seat.Value })
                .ToList(),
            ReservedSeats = eventAggregate.ReservedSeats
                .Select(seat => new EventReservedSeatEntity { EventId = eventAggregate.Id.Value, SeatNumber = seat.Value })
                .ToList()
        };
    }

    /// <summary>
    /// Applies aggregate state onto a tracked entity. Crucially, this is a *diff* — it only inserts
    /// or removes <see cref="EventReservedSeatEntity"/> rows that actually changed, and bumps the
    /// <see cref="EventSeatEntity.Version"/> concurrency token on exactly those seats. A naive
    /// clear-and-re-add would delete every seat row on every save and make the concurrency token
    /// always check against the default zero — silently defeating F6's optimistic concurrency.
    /// </summary>
    public static void Apply(Event eventAggregate, EventEntity entity)
    {
        entity.StartsAtUtc = eventAggregate.Schedule.StartsAtUtc;
        entity.EndsAtUtc = eventAggregate.Schedule.EndsAtUtc;
        entity.Organizer = eventAggregate.Organizer;
        entity.Status = (int)eventAggregate.Status;

        // The seat *configuration* (entity.Seats) is immutable after event creation; we never add
        // or remove seat rows here. We only bump their Version when their reservation state changes.

        var aggregateReserved = eventAggregate.ReservedSeats
            .Select(seat => seat.Value)
            .ToHashSet(StringComparer.Ordinal);

        var entityReserved = entity.ReservedSeats
            .ToDictionary(reservation => reservation.SeatNumber, StringComparer.Ordinal);

        var newlyReserved = aggregateReserved.Where(seat => !entityReserved.ContainsKey(seat)).ToArray();
        var newlyReleased = entityReserved.Keys.Where(seat => !aggregateReserved.Contains(seat)).ToArray();

        foreach (var seatNumber in newlyReserved)
        {
            entity.ReservedSeats.Add(new EventReservedSeatEntity
            {
                EventId = eventAggregate.Id.Value,
                SeatNumber = seatNumber
            });
        }

        foreach (var seatNumber in newlyReleased)
        {
            entity.ReservedSeats.Remove(entityReserved[seatNumber]);
        }

        // Bump the optimistic-concurrency token on every seat whose reservation state moved. A
        // concurrent reserver that loaded the same seat row with the old Version will fail its
        // UPDATE on SaveChanges → DbUpdateConcurrencyException → mapped to HTTP 409.
        var changedSeats = newlyReserved
            .Concat(newlyReleased)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var seat in entity.Seats.Where(s => changedSeats.Contains(s.SeatNumber)))
        {
            seat.Version++;
        }
    }
}
