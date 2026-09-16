using BuildingBlocks.Primitives;

namespace Booking.Service.Domain.ValueObjects;

public readonly record struct EventSchedule(DateTime StartsAtUtc, DateTime EndsAtUtc)
{
    public static Result<EventSchedule> Create(DateTime startsAtUtc, DateTime endsAtUtc)
    {
        if (endsAtUtc <= startsAtUtc)
        {
            return Result<EventSchedule>.Failure("Event end date must be after start date.");
        }

        return Result<EventSchedule>.Success(new EventSchedule(startsAtUtc, endsAtUtc));
    }
}
