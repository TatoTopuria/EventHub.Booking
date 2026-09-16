using Booking.Service.Domain.ValueObjects;

namespace Booking.Service.Application.Pricing;

/// <summary>
/// Implements one pricing rule (GoF Strategy pattern). Strategies are applied in
/// <see cref="Priority"/> order and may stack — early bird first, then group discount,
/// then VIP premium, etc.
/// </summary>
/// <remarks>
/// A strategy that does not apply to the given context MUST return the input amount unchanged
/// rather than throw. This keeps the resolver simple — every strategy participates in every
/// pricing decision and only adjusts when its predicate matches.
/// </remarks>
public interface IPricingStrategy
{
    /// <summary>
    /// Lower values run first. Use widely spaced numbers (e.g. 100, 200, 300) so additional
    /// strategies can be slotted in without renumbering.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Stable name used in logs and tests.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Applies the rule and returns the (possibly unchanged) amount.
    /// </summary>
    Money Apply(BookingPricingContext context, Money currentAmount);
}
