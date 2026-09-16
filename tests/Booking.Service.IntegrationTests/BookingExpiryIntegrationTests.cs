using BuildingBlocks.Abstractions.Messaging;
using Booking.Service.Application.Contracts.Persistence;
using Booking.Service.Domain.Aggregates;
using Booking.Service.Domain.Enums;
using Booking.Service.Domain.ValueObjects;
using Booking.Service.Infrastructure.Expiry;
using Booking.Service.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using BookingAggregate = Booking.Service.Domain.Aggregates.Booking;

namespace Booking.Service.IntegrationTests;

/// <summary>
/// End-to-end coverage for F5 — drives <see cref="BookingExpiryService"/> against the real EF Core
/// + Postgres stack to verify booking state, event state, and outbox emission all settle correctly.
/// </summary>
public sealed class BookingExpiryIntegrationTests(BookingApiFactory factory) : IClassFixture<BookingApiFactory>
{
    [Fact]
    public async Task Sweep_Should_Expire_Pending_Booking_Past_The_Window()
    {
        DockerRequirement.SkipIfUnavailable(factory.IsDockerAvailable);

        // Use a wall-clock CreatedAtUtc in the past instead of advancing a FakeTimeProvider — the
        // production SystemClockProvider will read DateTime.UtcNow and see the booking as expired.
        // Semantically equivalent to "advance 11 minutes" without coupling the test to the clock seam.
        var createdAtUtc = DateTime.UtcNow.AddMinutes(-15);

        Guid eventId;
        Guid bookingId;
        string seatValue;

        // Seed the event + pending booking via the real persistence stack so EF + outbox plumbing
        // is exercised exactly as it would be in production.
        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            var eventRepository = seedScope.ServiceProvider.GetRequiredService<IEventRepository>();
            var bookingRepository = seedScope.ServiceProvider.GetRequiredService<IBookingRepository>();
            var unitOfWork = seedScope.ServiceProvider.GetRequiredService<IBookingUnitOfWork>();

            var eventAggregate = BuildFreshEvent();
            await eventRepository.AddAsync(eventAggregate);

            var seat = eventAggregate.Seats.First();
            eventAggregate.MarkSeatReserved(seat);
            await eventRepository.UpdateAsync(eventAggregate);

            bookingId = Guid.NewGuid();
            var booking = BookingAggregate.Create(bookingId, eventAggregate.Id, CustomerId.New(), createdAtUtc).Value;
            booking.ReserveSeat(seat, createdAtUtc);
            await bookingRepository.AddAsync(booking);

            await unitOfWork.SaveChangesAsync();

            eventId = eventAggregate.Id.Value;
            seatValue = seat.Value;
        }

        // Drive a single sweep through the real production graph.
        BookingExpirySweepResult sweepResult;
        await using (var sweepScope = factory.Services.CreateAsyncScope())
        {
            var expiryService = sweepScope.ServiceProvider.GetRequiredService<BookingExpiryService>();
            sweepResult = await expiryService.RunOnceAsync(CancellationToken.None);
        }

        sweepResult.Skipped.Should().BeFalse();
        sweepResult.Failed.Should().Be(0);
        sweepResult.Expired.Should().BeGreaterThanOrEqualTo(1);

        // Verify booking + event state through a fresh context so we know it was persisted.
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<BookingDbContext>();

        var expiredBookingEntity = await verifyDb.Bookings
            .AsNoTracking()
            .Include(b => b.ReservedSeats)
            .SingleAsync(b => b.Id == bookingId);

        expiredBookingEntity.Status.Should().Be((int)BookingStatus.Expired);

        var refreshedEventEntity = await verifyDb.Events
            .AsNoTracking()
            .Include(e => e.ReservedSeats)
            .SingleAsync(e => e.Id == eventId);

        refreshedEventEntity.ReservedSeats.Should().NotContain(seatEntity => seatEntity.SeatNumber == seatValue);

        // Outbox is the boundary of Booking.Service's responsibility — confirm SeatReleased landed.
        var outboxRows = await verifyDb.OutboxMessages
            .AsNoTracking()
            .Where(message => message.Type == nameof(SeatReleasedIntegrationEvent))
            .ToListAsync();

        outboxRows.Should().Contain(message =>
            message.Payload.Contains(seatValue)
            && message.Payload.Contains(eventId.ToString("D")));
    }

    private static Event BuildFreshEvent()
    {
        // Build a fresh event aggregate independent of DevelopmentDataSeeder so this test does not
        // race with the seeded events the other suites use.
        var schedule = EventSchedule.Create(DateTime.UtcNow.AddDays(10), DateTime.UtcNow.AddDays(10).AddHours(2)).Value;
        var seats = new[]
        {
            SeatNumber.Create("X1").Value,
            SeatNumber.Create("X2").Value
        };

        var eventAggregate = Event.Create(EventId.New(), schedule, "ExpiryTest", seats).Value;
        eventAggregate.Publish();
        return eventAggregate;
    }
}
