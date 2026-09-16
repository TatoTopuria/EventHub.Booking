namespace Booking.Service.Application.Pricing;

/// <summary>
/// Default <see cref="IPricingStrategyResolver"/> — applies every registered strategy in priority order.
/// </summary>
/// <remarks>
/// Strategies that do not apply must be no-ops; the resolver does not try to filter them. This keeps the
/// strategy contract small and the resolver predictable.
/// </remarks>
public sealed class PricingStrategyResolver(IEnumerable<IPricingStrategy> strategies) : IPricingStrategyResolver
{
    private readonly IReadOnlyList<IPricingStrategy> _orderedStrategies = strategies
        .OrderBy(strategy => strategy.Priority)
        .ToArray();

    public PricingDecision Resolve(BookingPricingContext context)
    {
        var runningAmount = context.BaseAmount;
        var adjustments = new List<PricingAdjustment>(_orderedStrategies.Count);

        foreach (var strategy in _orderedStrategies)
        {
            var next = strategy.Apply(context, runningAmount);
            if (!next.Equals(runningAmount))
            {
                adjustments.Add(new PricingAdjustment(strategy.Name, next));
                runningAmount = next;
            }
        }

        return new PricingDecision(runningAmount, adjustments);
    }
}
