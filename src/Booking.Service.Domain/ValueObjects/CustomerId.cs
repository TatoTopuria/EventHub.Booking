using BuildingBlocks.Primitives;

namespace Booking.Service.Domain.ValueObjects;

public readonly record struct CustomerId(Guid Value)
{
    public static CustomerId New() => new(Guid.NewGuid());

    public static Result<CustomerId> Create(Guid value) =>
        value == Guid.Empty
            ? Result<CustomerId>.Failure("CustomerId cannot be empty.")
            : Result<CustomerId>.Success(new CustomerId(value));
}
