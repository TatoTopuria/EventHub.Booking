using Booking.Service.Domain.Aggregates;
using Booking.Service.Domain.DomainEvents;
using Booking.Service.Domain.Enums;
using Booking.Service.Domain.ValueObjects;
using Booking.Service.UnitTests.Builders;
using FluentAssertions;

using BookingAggregate = Booking.Service.Domain.Aggregates.Booking;

namespace Booking.Service.UnitTests;

public sealed class BookingAggregateTests
{
    [Fact]
    public void ReserveSeat_Should_Succeed_For_First_Reservation()
    {
        var eventAggregate = new EventBuilder().Published().Build();
        var seat = eventAggregate.Seats.First();
        var booking = CreateBooking();

        var result = booking.ReserveSeat(seat, DateTime.UtcNow);

        result.IsSuccess.Should().BeTrue();
        booking.ReservedSeats.Should().ContainSingle().Which.Should().Be(seat);
        booking.DomainEvents.Should().ContainSingle(@event => @event is SeatReserved);
    }

    [Fact]
    public void ReserveSeat_Should_Fail_When_Seat_Already_Reserved()
    {
        var eventAggregate = new EventBuilder().Published().Build();
        var seat = eventAggregate.Seats.First();
        var booking = CreateBooking();
        booking.ReserveSeat(seat, DateTime.UtcNow);

        var result = booking.ReserveSeat(seat, DateTime.UtcNow);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("cannot be reserved twice");
    }

    [Fact]
    public void Confirm_Should_Fail_When_Payment_Was_Not_Registered()
    {
        var booking = CreateBooking();
        booking.ReserveSeat(SeatNumber.Create("A1").Value, DateTime.UtcNow);

        var result = booking.Confirm(DateTime.UtcNow);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("without payment");
        booking.Status.Should().Be(BookingStatus.PendingPayment);
    }

    [Fact]
    public void Confirm_Should_Succeed_When_Payment_Was_Registered()
    {
        var booking = CreateBooking();
        booking.ReserveSeat(SeatNumber.Create("A1").Value, DateTime.UtcNow);
        booking.RegisterPayment(Money.Create(120, "usd").Value);

        var result = booking.Confirm(DateTime.UtcNow);

        result.IsSuccess.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Confirmed);
        booking.DomainEvents.Should().Contain(@event => @event is BookingConfirmed);
    }

    [Fact]
    public void ExpireIfUnpaid_Should_Fail_Before_Ten_Minutes()
    {
        var createdAtUtc = DateTime.UtcNow;
        var booking = CreateBooking(createdAtUtc);
        booking.ReserveSeat(SeatNumber.Create("A1").Value, createdAtUtc);

        var result = booking.ExpireIfUnpaid(createdAtUtc.AddMinutes(9));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("10 unpaid minutes");
        booking.Status.Should().Be(BookingStatus.PendingPayment);
    }

    [Fact]
    public void ExpireIfUnpaid_Should_Succeed_After_Ten_Minutes()
    {
        var createdAtUtc = DateTime.UtcNow;
        var booking = CreateBooking(createdAtUtc);
        booking.ReserveSeat(SeatNumber.Create("A1").Value, createdAtUtc);

        var result = booking.ExpireIfUnpaid(createdAtUtc.AddMinutes(10));

        result.IsSuccess.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Expired);
        booking.DomainEvents.Should().Contain(@event => @event is BookingExpired);
    }

    [Fact]
    public void ExpireIfUnpaid_Should_Emit_SeatReleased_For_Every_Reserved_Seat()
    {
        var createdAtUtc = DateTime.UtcNow;
        var booking = CreateBooking(createdAtUtc);
        booking.ReserveSeat(SeatNumber.Create("A1").Value, createdAtUtc);
        booking.ReserveSeat(SeatNumber.Create("A2").Value, createdAtUtc);

        var expireAt = createdAtUtc.AddMinutes(10);
        var result = booking.ExpireIfUnpaid(expireAt);

        result.IsSuccess.Should().BeTrue();
        booking.DomainEvents.OfType<SeatReleased>()
            .Select(seatReleased => seatReleased.SeatNumber.Value)
            .Should().BeEquivalentTo(new[] { "A1", "A2" });
        booking.DomainEvents.OfType<BookingExpired>().Should().HaveCount(1);
    }

    [Fact]
    public void ExpireIfUnpaid_Should_Fail_For_Paid_Booking()
    {
        var createdAtUtc = DateTime.UtcNow;
        var booking = CreateBooking(createdAtUtc);
        booking.ReserveSeat(SeatNumber.Create("A1").Value, createdAtUtc);
        booking.RegisterPayment(Money.Create(70, "usd").Value);

        var result = booking.ExpireIfUnpaid(createdAtUtc.AddMinutes(11));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("paid booking");
    }

    [Fact]
    public void Cancel_Should_Emit_BookingCancelled_Event()
    {
        var booking = CreateBooking();
        booking.ReserveSeat(SeatNumber.Create("A1").Value, DateTime.UtcNow);

        var result = booking.Cancel(DateTime.UtcNow);

        result.IsSuccess.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Cancelled);
        booking.DomainEvents.Should().Contain(@event => @event is BookingCancelled);
        booking.DomainEvents.Should().Contain(@event => @event is SeatReleased);
    }

    private static BookingAggregate CreateBooking(DateTime? createdAtUtc = null)
    {
        var eventId = EventId.New();
        var customerId = CustomerId.New();
        var result = BookingAggregate.Create(Guid.NewGuid(), eventId, customerId, createdAtUtc ?? DateTime.UtcNow);
        return result.Value;
    }
}
