using Booking.Service.Domain.ValueObjects;

namespace Booking.Service.Application.Pricing;

/// <summary>
/// Orchestrates the registered <see cref="IPricingStrategy"/> instances, applying them in priority order.
/// </summary>
public interface IPricingStrategyResolver
{
    /// <summary>
    /// Walks the strategy chain in <see cref="IPricingStrategy.Priority"/> order, threading the running
    /// amount through each call. Returns the final price and the breakdown for audit logging.
    /// </summary>
    PricingDecision Resolve(BookingPricingContext context);
}

/// <summary>
/// Captures the final amount plus a per-strategy trail. Useful for surfacing "why was I charged X?".
/// </summary>
public sealed record PricingDecision(Money FinalAmount, IReadOnlyList<PricingAdjustment> Adjustments);

/// <summary>
/// Single step inside a <see cref="PricingDecision"/>: which strategy ran and what it produced.
/// </summary>
public sealed record PricingAdjustment(string StrategyName, Money AmountAfter);
