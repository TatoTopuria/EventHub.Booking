using BuildingBlocks.Primitives;

namespace Booking.Service.Domain.ValueObjects;

public readonly record struct SeatNumber(string Value)
{
    public static Result<SeatNumber> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<SeatNumber>.Failure("Seat number cannot be empty.");
        }

        return Result<SeatNumber>.Success(new SeatNumber(value.Trim().ToUpperInvariant()));
    }
}
