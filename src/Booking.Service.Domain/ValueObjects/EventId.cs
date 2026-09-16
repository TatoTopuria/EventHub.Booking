using BuildingBlocks.Primitives;

namespace Booking.Service.Domain.ValueObjects;

public readonly record struct EventId(Guid Value)
{
    public static EventId New() => new(Guid.NewGuid());

    public static Result<EventId> Create(Guid value) =>
        value == Guid.Empty
            ? Result<EventId>.Failure("EventId cannot be empty.")
            : Result<EventId>.Success(new EventId(value));
}
