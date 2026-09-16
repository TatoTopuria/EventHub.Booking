using Booking.Service.Domain.DomainEvents;
using Booking.Service.Domain.Enums;
using Booking.Service.Domain.ValueObjects;
using BuildingBlocks.Domain;
using BuildingBlocks.Primitives;

namespace Booking.Service.Domain.Aggregates;

public sealed class Event : AggregateRoot<EventId>
{
    private readonly HashSet<SeatNumber> _seats;
    private readonly HashSet<SeatNumber> _reservedSeats;

    private Event(EventId id, EventSchedule schedule, string organizer, IEnumerable<SeatNumber> seats)
        : base(id)
    {
        Schedule = schedule;
        Organizer = organizer;
        Status = EventStatus.Draft;
        _seats = seats.ToHashSet();
        _reservedSeats = [];
    }

    public EventSchedule Schedule { get; private set; }

    public string Organizer { get; private set; }

    public EventStatus Status { get; private set; }

    public IReadOnlyCollection<SeatNumber> Seats => _seats;

    public IReadOnlyCollection<SeatNumber> ReservedSeats => _reservedSeats;

    public static Result<Event> Create(EventId id, EventSchedule schedule, string organizer, IEnumerable<SeatNumber> seats)
    {
        var seatList = seats.ToList();

        if (string.IsNullOrWhiteSpace(organizer))
        {
            return Result<Event>.Failure("Organizer is required.");
        }

        if (seatList.Count == 0)
        {
            return Result<Event>.Failure("At least one seat is required.");
        }

        if (seatList.Distinct().Count() != seatList.Count)
        {
            return Result<Event>.Failure("Seat numbers must be unique.");
        }

        return Result<Event>.Success(new Event(id, schedule, organizer.Trim(), seatList));
    }

    public Result Publish()
    {
        if (Status != EventStatus.Draft)
        {
            return Result.Failure("Only draft events can be published.");
        }

        Status = EventStatus.Published;
        return Result.Success();
    }

    public Result Cancel()
    {
        if (Status == EventStatus.Cancelled)
        {
            return Result.Failure("Event is already cancelled.");
        }

        Status = EventStatus.Cancelled;
        return Result.Success();
    }

    public bool CanReserveSeat(SeatNumber seatNumber)
    {
        return Status == EventStatus.Published
            && _seats.Contains(seatNumber)
            && !_reservedSeats.Contains(seatNumber);
    }

    public Result MarkSeatReserved(SeatNumber seatNumber)
    {
        if (!CanReserveSeat(seatNumber))
        {
            return Result.Failure("Seat cannot be reserved for this event.");
        }

        _reservedSeats.Add(seatNumber);
        return Result.Success();
    }

    public Result MarkSeatReleased(SeatNumber seatNumber)
    {
        if (!_reservedSeats.Contains(seatNumber))
        {
            return Result.Failure("Seat is not reserved for this event.");
        }

        _reservedSeats.Remove(seatNumber);
        return Result.Success();
    }

    /// <summary>
    /// Moves the event to a new schedule. Raises <see cref="EventUpdated"/> so downstream
    /// projections (Catalog cache, Realtime, Analytics) can react.
    /// </summary>
    /// <remarks>
    /// Forbidden once the event is cancelled — a cancelled event is terminal. Also a no-op when
    /// the new schedule equals the current one, so a re-issued PATCH does not emit a spurious
    /// integration event that would invalidate caches for nothing.
    /// </remarks>
    public Result Reschedule(EventSchedule newSchedule, DateTime occurredOnUtc)
    {
        if (Status == EventStatus.Cancelled)
        {
            return Result.Failure("A cancelled event cannot be rescheduled.");
        }

        if (newSchedule.Equals(Schedule))
        {
            return Result.Success();
        }

        Schedule = newSchedule;
        AddDomainEvent(new EventUpdated(Id, occurredOnUtc));
        return Result.Success();
    }

    /// <summary>
    /// Reassigns the event to a new organizer. Same semantics as <see cref="Reschedule"/> — no-op
    /// when unchanged, refused after cancellation, raises <see cref="EventUpdated"/> otherwise.
    /// </summary>
    public Result ChangeOrganizer(string newOrganizer, DateTime occurredOnUtc)
    {
        if (Status == EventStatus.Cancelled)
        {
            return Result.Failure("A cancelled event cannot change organizer.");
        }

        if (string.IsNullOrWhiteSpace(newOrganizer))
        {
            return Result.Failure("Organizer is required.");
        }

        var normalized = newOrganizer.Trim();
        if (string.Equals(normalized, Organizer, StringComparison.Ordinal))
        {
            return Result.Success();
        }

        Organizer = normalized;
        AddDomainEvent(new EventUpdated(Id, occurredOnUtc));
        return Result.Success();
    }
}
