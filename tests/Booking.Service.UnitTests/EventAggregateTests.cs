using Booking.Service.Domain.DomainEvents;
using Booking.Service.Domain.Enums;
using Booking.Service.Domain.ValueObjects;
using Booking.Service.UnitTests.Builders;
using FluentAssertions;

namespace Booking.Service.UnitTests;

public sealed class EventAggregateTests
{
    [Fact]
    public void MarkSeatReserved_Should_Succeed_When_Event_Is_Published_And_Seat_Is_Available()
    {
        var eventAggregate = new EventBuilder().Published().WithSeats("A1", "A2").Build();
        var seat = SeatNumber.Create("A1").Value;

        var result = eventAggregate.MarkSeatReserved(seat);

        result.IsSuccess.Should().BeTrue();
        eventAggregate.ReservedSeats.Should().Contain(seat);
    }

    [Fact]
    public void MarkSeatReserved_Should_Fail_When_Event_Is_Not_Published()
    {
        var eventAggregate = new EventBuilder().WithSeats("A1", "A2").Build();
        var seat = SeatNumber.Create("A1").Value;

        var result = eventAggregate.MarkSeatReserved(seat);

        result.IsFailure.Should().BeTrue();
        eventAggregate.Status.Should().Be(EventStatus.Draft);
    }

    [Fact]
    public void Reschedule_Should_Update_Schedule_And_Raise_EventUpdated()
    {
        var eventAggregate = new EventBuilder().Published().Build();
        var newSchedule = EventSchedule.Create(
            DateTime.UtcNow.AddDays(3),
            DateTime.UtcNow.AddDays(3).AddHours(2)).Value;
        var occurredOn = DateTime.UtcNow;

        var result = eventAggregate.Reschedule(newSchedule, occurredOn);

        result.IsSuccess.Should().BeTrue();
        eventAggregate.Schedule.Should().Be(newSchedule);
        eventAggregate.DomainEvents.OfType<EventUpdated>()
            .Should().ContainSingle(e => e.EventId == eventAggregate.Id && e.OccurredOnUtc == occurredOn);
    }

    [Fact]
    public void Reschedule_Should_Be_NoOp_When_Schedule_Unchanged()
    {
        var eventAggregate = new EventBuilder().Published().Build();
        var sameSchedule = eventAggregate.Schedule;

        var result = eventAggregate.Reschedule(sameSchedule, DateTime.UtcNow);

        result.IsSuccess.Should().BeTrue();
        eventAggregate.DomainEvents.OfType<EventUpdated>().Should().BeEmpty(
            "an unchanged reschedule must not emit a spurious integration event that would invalidate caches for nothing");
    }

    [Fact]
    public void Reschedule_Should_Fail_When_Event_Cancelled()
    {
        var eventAggregate = new EventBuilder().Published().Build();
        eventAggregate.Cancel();
        eventAggregate.ClearDomainEvents();

        var newSchedule = EventSchedule.Create(
            DateTime.UtcNow.AddDays(3),
            DateTime.UtcNow.AddDays(3).AddHours(2)).Value;

        var result = eventAggregate.Reschedule(newSchedule, DateTime.UtcNow);

        result.IsFailure.Should().BeTrue();
        eventAggregate.DomainEvents.OfType<EventUpdated>().Should().BeEmpty();
    }

    [Fact]
    public void ChangeOrganizer_Should_Update_Organizer_And_Raise_EventUpdated()
    {
        var eventAggregate = new EventBuilder().Published().WithOrganizer("Original").Build();
        var occurredOn = DateTime.UtcNow;

        var result = eventAggregate.ChangeOrganizer("  New Organizer  ", occurredOn);

        result.IsSuccess.Should().BeTrue();
        eventAggregate.Organizer.Should().Be("New Organizer", "organizer is trimmed on save");
        eventAggregate.DomainEvents.OfType<EventUpdated>()
            .Should().ContainSingle(e => e.OccurredOnUtc == occurredOn);
    }

    [Fact]
    public void ChangeOrganizer_Should_Be_NoOp_When_Same_Name_Whitespace_Padded()
    {
        var eventAggregate = new EventBuilder().Published().WithOrganizer("EventHub").Build();

        var result = eventAggregate.ChangeOrganizer("  EventHub  ", DateTime.UtcNow);

        result.IsSuccess.Should().BeTrue();
        eventAggregate.DomainEvents.OfType<EventUpdated>().Should().BeEmpty();
    }

    [Fact]
    public void ChangeOrganizer_Should_Fail_When_Blank()
    {
        var eventAggregate = new EventBuilder().Published().Build();

        var result = eventAggregate.ChangeOrganizer("   ", DateTime.UtcNow);

        result.IsFailure.Should().BeTrue();
        eventAggregate.DomainEvents.OfType<EventUpdated>().Should().BeEmpty();
    }
}
