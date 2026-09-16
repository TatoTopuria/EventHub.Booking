using Booking.Service.Domain.Aggregates;
using Booking.Service.Domain.ValueObjects;
using Booking.Service.Infrastructure.Persistence.Mappers;
using Microsoft.EntityFrameworkCore;

namespace Booking.Service.Infrastructure.Persistence;

public static class DevelopmentDataSeeder
{
    public static async Task SeedAsync(BookingDbContext dbContext, CancellationToken cancellationToken = default)
    {
        if (await dbContext.Events.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = DateTime.UtcNow.Date.AddHours(9);

        var events = new[]
        {
            CreateSampleEvent(Guid.Parse("11111111-1111-1111-1111-111111111111"), "MainHall", now.AddDays(2), now.AddDays(2).AddHours(2), ["A1", "A2", "A3", "B1", "F4-A1", "F4-A2", "F4-A3", "F4-B1", "F4-B2", "F4-B3"]),
            CreateSampleEvent(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Studio", now.AddDays(5), now.AddDays(5).AddHours(3), ["S1", "S2", "S3"]),
            CreateSampleEvent(Guid.Parse("33333333-3333-3333-3333-333333333333"), "Arena", now.AddDays(7), now.AddDays(7).AddHours(4), ["R1", "R2", "R3", "R4", "R5"])
        };

        await dbContext.Events.AddRangeAsync(events.Select(EventMapper.ToEntity), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static Event CreateSampleEvent(Guid eventId, string organizer, DateTime startsAtUtc, DateTime endsAtUtc, IReadOnlyCollection<string> seats)
    {
        var eventAggregate = Event.Create(
            EventId.Create(eventId).Value,
            EventSchedule.Create(startsAtUtc, endsAtUtc).Value,
            organizer,
            seats.Select(seat => SeatNumber.Create(seat).Value)).Value;

        eventAggregate.Publish();

        return eventAggregate;
    }
}
