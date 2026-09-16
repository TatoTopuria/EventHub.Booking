using Booking.Service.Domain.DomainEvents;
using Booking.Service.Domain.Enums;
using Booking.Service.Domain.ValueObjects;
using BuildingBlocks.Domain;
using BuildingBlocks.Primitives;

namespace Booking.Service.Domain.Aggregates;

public sealed class Booking : AggregateRoot<Guid>
{
    private readonly List<SeatNumber> _reservedSeats;

    private Booking(Guid id, EventId eventId, CustomerId customerId, DateTime createdAtUtc)
        : base(id)
    {
        EventId = eventId;
        CustomerId = customerId;
        CreatedAtUtc = createdAtUtc;
        Status = BookingStatus.PendingPayment;
        _reservedSeats = [];
    }

    public EventId EventId { get; private set; }

    public CustomerId CustomerId { get; private set; }

    public BookingStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? ConfirmedAtUtc { get; private set; }

    public DateTime? ExpiresAtUtc => CreatedAtUtc.AddMinutes(10);

    public bool IsPaid { get; private set; }

    public Money? PaymentAmount { get; private set; }

    public IReadOnlyCollection<SeatNumber> ReservedSeats => _reservedSeats.AsReadOnly();

    public static Result<Booking> Create(Guid id, EventId eventId, CustomerId customerId, DateTime createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            return Result<Booking>.Failure("Booking id cannot be empty.");
        }

        return Result<Booking>.Success(new Booking(id, eventId, customerId, createdAtUtc));
    }

    public Result ReserveSeat(SeatNumber seatNumber, DateTime occurredOnUtc)
    {
        if (Status != BookingStatus.PendingPayment)
        {
            return Result.Failure("Only pending bookings can reserve seats.");
        }

        if (_reservedSeats.Contains(seatNumber))
        {
            return Result.Failure("A seat cannot be reserved twice.");
        }

        _reservedSeats.Add(seatNumber);
        AddDomainEvent(new SeatReserved(Id, EventId, CustomerId, seatNumber, occurredOnUtc));
        return Result.Success();
    }

    public Result RegisterPayment(Money amount)
    {
        if (Status != BookingStatus.PendingPayment)
        {
            return Result.Failure("Only pending bookings can register payment.");
        }

        IsPaid = true;
        PaymentAmount = amount;
        return Result.Success();
    }

    public Result Confirm(DateTime occurredOnUtc)
    {
        if (Status != BookingStatus.PendingPayment)
        {
            return Result.Failure("Only pending bookings can be confirmed.");
        }

        if (!IsPaid)
        {
            return Result.Failure("A booking cannot be confirmed without payment.");
        }

        Status = BookingStatus.Confirmed;
        ConfirmedAtUtc = occurredOnUtc;
        AddDomainEvent(new BookingConfirmed(Id, EventId, CustomerId, occurredOnUtc));
        return Result.Success();
    }

    public Result ExpireIfUnpaid(DateTime utcNow)
    {
        if (Status != BookingStatus.PendingPayment)
        {
            return Result.Failure("Only pending bookings can expire.");
        }

        if (IsPaid)
        {
            return Result.Failure("A paid booking cannot expire.");
        }

        if (utcNow < CreatedAtUtc.AddMinutes(10))
        {
            return Result.Failure("A booking expires only after 10 unpaid minutes.");
        }

        Status = BookingStatus.Expired;

        // Mirror Cancel: release every reserved seat so downstream consumers (Realtime hub, analytics)
        // see the seats become available again. The Event aggregate is updated separately by the
        // expiry worker, just like CancelBookingCommandHandler updates it for cancellation.
        foreach (var reservedSeat in _reservedSeats)
        {
            AddDomainEvent(new SeatReleased(Id, EventId, CustomerId, reservedSeat, utcNow));
        }

        AddDomainEvent(new BookingExpired(Id, EventId, CustomerId, utcNow));
        return Result.Success();
    }

    public Result Cancel(DateTime occurredOnUtc)
    {
        if (Status is BookingStatus.Cancelled or BookingStatus.Expired)
        {
            return Result.Failure("Booking cannot be cancelled from the current state.");
        }

        Status = BookingStatus.Cancelled;

        foreach (var reservedSeat in _reservedSeats)
        {
            AddDomainEvent(new SeatReleased(Id, EventId, CustomerId, reservedSeat, occurredOnUtc));
        }

        AddDomainEvent(new BookingCancelled(Id, EventId, CustomerId, occurredOnUtc));
        return Result.Success();
    }
}
