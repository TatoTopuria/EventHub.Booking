using Booking.Service.Domain.Aggregates;
using Booking.Service.Domain.ValueObjects;
using Booking.Service.Infrastructure.Persistence.Mappers;
using Booking.Service.Infrastructure.Persistence.Models;
using FluentAssertions;

namespace Booking.Service.UnitTests.Concurrency;

/// <summary>
/// Verifies that <see cref="EventMapper.Apply"/> only bumps the concurrency token on seats whose
/// reservation state actually changed. Two reservers racing for different seats must not bump each
/// other's Version (which would force every save to conflict with every other save).
/// </summary>
public sealed class EventMapperVersionBumpTests
{
    [Fact]
    public void Apply_Should_Bump_Version_On_Newly_Reserved_Seat_Only()
    {
        var (entity, eventAggregate) = BuildPair("A1", "A2", "A3");

        eventAggregate.MarkSeatReserved(SeatNumber.Create("A2").Value);

        EventMapper.Apply(eventAggregate, entity);

        SeatVersion(entity, "A1").Should().Be(0u);
        SeatVersion(entity, "A2").Should().Be(1u);
        SeatVersion(entity, "A3").Should().Be(0u);
    }

    [Fact]
    public void Apply_Should_Bump_Version_On_Released_Seat_Only()
    {
        var (entity, eventAggregate) = BuildPair("A1", "A2", "A3");
        eventAggregate.MarkSeatReserved(SeatNumber.Create("A1").Value);
        EventMapper.Apply(eventAggregate, entity); // baseline: A1 reserved, Version[A1]=1

        eventAggregate.MarkSeatReleased(SeatNumber.Create("A1").Value);
        EventMapper.Apply(eventAggregate, entity);

        SeatVersion(entity, "A1").Should().Be(2u);
        SeatVersion(entity, "A2").Should().Be(0u);
        SeatVersion(entity, "A3").Should().Be(0u);
    }

    [Fact]
    public void Apply_Should_Not_Bump_Any_Seat_When_No_Reservation_State_Changed()
    {
        var (entity, eventAggregate) = BuildPair("A1", "A2", "A3");

        EventMapper.Apply(eventAggregate, entity);

        entity.Seats.Select(seat => seat.Version).Should().AllBeEquivalentTo(0u);
    }

    [Fact]
    public void Apply_Should_Insert_New_Reservation_Row_Only_When_Seat_Becomes_Reserved()
    {
        var (entity, eventAggregate) = BuildPair("A1", "A2");
        eventAggregate.MarkSeatReserved(SeatNumber.Create("A1").Value);

        EventMapper.Apply(eventAggregate, entity);

        entity.ReservedSeats.Should().HaveCount(1);
        entity.ReservedSeats.Single().SeatNumber.Should().Be("A1");
    }

    [Fact]
    public void Apply_Should_Remove_Reservation_Row_When_Seat_Is_Released()
    {
        var (entity, eventAggregate) = BuildPair("A1", "A2");
        eventAggregate.MarkSeatReserved(SeatNumber.Create("A1").Value);
        EventMapper.Apply(eventAggregate, entity);

        eventAggregate.MarkSeatReleased(SeatNumber.Create("A1").Value);
        EventMapper.Apply(eventAggregate, entity);

        entity.ReservedSeats.Should().BeEmpty();
    }

    private static uint SeatVersion(EventEntity entity, string seatNumber) =>
        entity.Seats.Single(seat => seat.SeatNumber == seatNumber).Version;

    /// <summary>
    /// Builds a Published event aggregate plus a matching EventEntity in the same state EF would
    /// have after GetByIdAsync — Version starts at 0 and no seats are reserved.
    /// </summary>
    private static (EventEntity Entity, Event Aggregate) BuildPair(params string[] seats)
    {
        var eventId = EventId.New();
        var schedule = EventSchedule.Create(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(2)).Value;
        var seatValues = seats.Select(seat => SeatNumber.Create(seat).Value).ToArray();
        var eventAggregate = Event.Create(eventId, schedule, "MapperTest", seatValues).Value;
        eventAggregate.Publish();

        var entity = new EventEntity
        {
            Id = eventId.Value,
            StartsAtUtc = schedule.StartsAtUtc,
            EndsAtUtc = schedule.EndsAtUtc,
            Organizer = "MapperTest",
            Status = (int)Booking.Service.Domain.Enums.EventStatus.Published,
            Seats = seats
                .Select(seat => new EventSeatEntity { EventId = eventId.Value, SeatNumber = seat, Version = 0u })
                .ToList(),
            ReservedSeats = new List<EventReservedSeatEntity>()
        };

        return (entity, eventAggregate);
    }
}
