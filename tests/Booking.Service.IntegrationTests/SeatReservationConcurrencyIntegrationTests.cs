using Booking.Service.Application.Bookings.Commands.ReserveSeat;
using Booking.Service.Application.Contracts.Persistence;
using Booking.Service.Domain.Aggregates;
using Booking.Service.Domain.ValueObjects;
using Booking.Service.Infrastructure.Persistence;
using BuildingBlocks.Primitives;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Service.IntegrationTests;

/// <summary>
/// F6 stress test — fires N parallel ReserveSeatCommand invocations against the same seat and asserts
/// exactly one wins. The defense-in-depth (distributed lock + EF concurrency token) means losers
/// fail cleanly and never leave orphan booking rows.
/// </summary>
public sealed class SeatReservationConcurrencyIntegrationTests(BookingApiFactory factory)
    : IClassFixture<BookingApiFactory>
{
    private const int ContenderCount = 50;

    [Fact]
    public async Task Fifty_Concurrent_Reservers_Should_Produce_Exactly_One_Booking()
    {
        DockerRequirement.SkipIfUnavailable(factory.IsDockerAvailable);

        // Seed a fresh event with a single contested seat so this test does not race with the
        // dev-seeded events used by the other integration suites.
        var eventId = await SeedEventWithSingleSeatAsync("Z1");
        var customerId = Guid.NewGuid();

        // ISender is scoped, so each parallel task creates its own scope + handler instance.
        var sweepResults = await Task.WhenAll(Enumerable.Range(0, ContenderCount)
            .Select(_ => Task.Run(() => SendReserveAsync(eventId, customerId, "Z1"))));

        var successes = sweepResults.Where(result => result.IsSuccess).ToArray();
        var failures = sweepResults.Where(result => result.IsFailure).ToArray();

        successes.Should().HaveCount(1, "exactly one reserver may win the seat");
        failures.Should().HaveCount(ContenderCount - 1, "every other contender must fail cleanly");

        // No orphan bookings — the database should have exactly one row tied to this event.
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<BookingDbContext>();

        var bookingsForEvent = await db.Bookings
            .AsNoTracking()
            .Include(booking => booking.ReservedSeats)
            .Where(booking => booking.EventId == eventId)
            .ToListAsync();

        bookingsForEvent.Should().ContainSingle("the 49 losers must NOT have persisted booking rows");
        bookingsForEvent.Single().ReservedSeats.Should().ContainSingle(rs => rs.SeatNumber == "Z1");

        // The Event aggregate's reserved-seats set should reflect exactly the one winner.
        var eventEntity = await db.Events
            .AsNoTracking()
            .Include(eventEntity => eventEntity.ReservedSeats)
            .Include(eventEntity => eventEntity.Seats)
            .SingleAsync(eventEntity => eventEntity.Id == eventId);

        eventEntity.ReservedSeats.Should().ContainSingle(rs => rs.SeatNumber == "Z1");
        eventEntity.Seats.Single(seat => seat.SeatNumber == "Z1").Version.Should().BeGreaterThan(0u,
            "the winning reservation should have bumped the concurrency token");
    }

    private async Task<Result<Guid>> SendReserveAsync(Guid eventId, Guid customerId, string seat)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        return await sender.Send(new ReserveSeatCommand(eventId, customerId, seat));
    }

    private async Task<Guid> SeedEventWithSingleSeatAsync(string seatNumber)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var eventRepository = scope.ServiceProvider.GetRequiredService<IEventRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IBookingUnitOfWork>();

        var schedule = EventSchedule.Create(DateTime.UtcNow.AddDays(7), DateTime.UtcNow.AddDays(7).AddHours(2)).Value;
        var eventAggregate = Event.Create(
            EventId.New(),
            schedule,
            "ConcurrencyTest",
            new[] { SeatNumber.Create(seatNumber).Value }).Value;
        eventAggregate.Publish();

        await eventRepository.AddAsync(eventAggregate);
        await unitOfWork.SaveChangesAsync();

        return eventAggregate.Id.Value;
    }
}
