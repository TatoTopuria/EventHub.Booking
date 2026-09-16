using Booking.Service.Application.Pricing;
using Booking.Service.Domain.ValueObjects;
using BuildingBlocks.Time;
using Microsoft.Extensions.Options;

namespace Booking.Service.Infrastructure.Pricing;

/// <summary>
/// Applies a percentage discount when the booking is placed at least N days before the event.
/// </summary>
public sealed class EarlyBirdPricingStrategy : IPricingStrategy
{
    public const string StrategyName = "EarlyBird";

    private readonly IClockProvider _clock;
    private readonly EarlyBirdOptions _options;

    public EarlyBirdPricingStrategy(IClockProvider clock, IOptions<BookingPricingOptions> options)
    {
        _clock = clock;
        _options = options.Value.EarlyBird;
    }

    /// <summary>
    /// Runs first so subsequent strategies (group, VIP) build on the discounted base.
    /// </summary>
    public int Priority => 100;

    public string Name => StrategyName;

    public Money Apply(BookingPricingContext context, Money currentAmount)
    {
        if (!_options.Enabled || _options.DiscountPercent <= 0m)
        {
            return currentAmount;
        }

        var earliestQualifying = context.EventStartsAtUtc.AddDays(-_options.DaysBeforeEventStart);
        if (_clock.GetUtcNow() > earliestQualifying)
        {
            return currentAmount;
        }

        var multiplier = 1m - (_options.DiscountPercent / 100m);
        var discounted = Money.Create(Round(currentAmount.Amount * multiplier), currentAmount.Currency);
        return discounted.IsSuccess ? discounted.Value : currentAmount;
    }

    private static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.ToEven);
}
