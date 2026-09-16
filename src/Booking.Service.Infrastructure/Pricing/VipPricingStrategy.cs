using Booking.Service.Application.Pricing;
using Booking.Service.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace Booking.Service.Infrastructure.Pricing;

/// <summary>
/// Applies a percentage surcharge when the customer's tier (looked up from
/// <see cref="BookingPricingContext.Metadata"/>) matches the configured VIP tier value.
/// </summary>
public sealed class VipPricingStrategy : IPricingStrategy
{
    public const string StrategyName = "Vip";

    private readonly VipOptions _options;

    public VipPricingStrategy(IOptions<BookingPricingOptions> options)
    {
        _options = options.Value.Vip;
    }

    /// <summary>
    /// Runs last so the surcharge is calculated on the already-discounted base.
    /// </summary>
    public int Priority => 300;

    public string Name => StrategyName;

    public Money Apply(BookingPricingContext context, Money currentAmount)
    {
        if (!_options.Enabled || _options.SurchargePercent <= 0m)
        {
            return currentAmount;
        }

        if (!context.Metadata.TryGetValue(_options.TierMetadataKey, out var tier)
            || !string.Equals(tier, _options.TierValue, StringComparison.OrdinalIgnoreCase))
        {
            return currentAmount;
        }

        var multiplier = 1m + (_options.SurchargePercent / 100m);
        var increased = Money.Create(Round(currentAmount.Amount * multiplier), currentAmount.Currency);
        return increased.IsSuccess ? increased.Value : currentAmount;
    }

    private static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.ToEven);
}
