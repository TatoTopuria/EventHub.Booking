using Booking.Service.Domain.Aggregates;
using Booking.Service.Domain.ValueObjects;

namespace Booking.Service.UnitTests.Builders;

public sealed class EventBuilder
{
    private EventId _eventId = EventId.New();
    private EventSchedule _schedule = EventSchedule.Create(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(2)).Value;
    private string _organizer = "EventHub Organizer";
    private readonly List<SeatNumber> _seats =
    [
        SeatNumber.Create("A1").Value,
        SeatNumber.Create("A2").Value,
        SeatNumber.Create("A3").Value
    ];
    private bool _publish;

    public EventBuilder WithId(EventId eventId)
    {
        _eventId = eventId;
        return this;
    }

    public EventBuilder WithOrganizer(string organizer)
    {
        _organizer = organizer;
        return this;
    }

    public EventBuilder WithSchedule(DateTime startsAtUtc, DateTime endsAtUtc)
    {
        _schedule = EventSchedule.Create(startsAtUtc, endsAtUtc).Value;
        return this;
    }

    public EventBuilder WithSeats(params string[] seatNumbers)
    {
        _seats.Clear();
        _seats.AddRange(seatNumbers.Select(seat => SeatNumber.Create(seat).Value));
        return this;
    }

    public EventBuilder Published()
    {
        _publish = true;
        return this;
    }

    public Event Build()
    {
        var eventResult = Event.Create(_eventId, _schedule, _organizer, _seats);
        if (eventResult.IsFailure)
        {
            throw new InvalidOperationException(eventResult.Error);
        }

        var eventAggregate = eventResult.Value;

        if (_publish)
        {
            var publishResult = eventAggregate.Publish();
            if (publishResult.IsFailure)
            {
                throw new InvalidOperationException(publishResult.Error);
            }
        }

        return eventAggregate;
    }
}
