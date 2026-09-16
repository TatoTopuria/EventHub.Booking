using BuildingBlocks.Primitives;

namespace Booking.Service.Domain.ValueObjects;

public readonly record struct Money(decimal Amount, string Currency)
{
    public static Result<Money> Create(decimal amount, string currency)
    {
        if (amount <= 0)
        {
            return Result<Money>.Failure("Amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            return Result<Money>.Failure("Currency must be a 3-letter ISO code.");
        }

        return Result<Money>.Success(new Money(amount, currency.Trim().ToUpperInvariant()));
    }
}
