using Booking.Service.Application.Pricing;
using Booking.Service.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace Booking.Service.Infrastructure.Pricing;

/// <summary>
/// Applies a percentage discount when a single booking reserves N or more seats together.
/// </summary>
public sealed class GroupDiscountPricingStrategy : IPricingStrategy
{
    public const string StrategyName = "GroupDiscount";

    private readonly GroupDiscountOptions _options;

    public GroupDiscountPricingStrategy(IOptions<BookingPricingOptions> options)
    {
        _options = options.Value.GroupDiscount;
    }

    /// <summary>
    /// Runs after early bird (so the discount stacks) but before VIP (so VIP premium applies to the
    /// already-discounted base — matches the spec's intent that VIP customers pay a premium, not a
    /// premium on a non-discounted price).
    /// </summary>
    public int Priority => 200;

    public string Name => StrategyName;

    public Money Apply(BookingPricingContext context, Money currentAmount)
    {
        if (!_options.Enabled || _options.DiscountPercent <= 0m)
        {
            return currentAmount;
        }

        if (context.SeatCount < _options.MinimumSeats)
        {
            return currentAmount;
        }

        var multiplier = 1m - (_options.DiscountPercent / 100m);
        var discounted = Money.Create(Round(currentAmount.Amount * multiplier), currentAmount.Currency);
        return discounted.IsSuccess ? discounted.Value : currentAmount;
    }

    private static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.ToEven);
}
