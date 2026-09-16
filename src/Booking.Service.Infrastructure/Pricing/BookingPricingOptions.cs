namespace Booking.Service.Infrastructure.Pricing;

/// <summary>
/// Configuration for the three built-in pricing strategies. Defaults are intentionally conservative
/// so unconfigured services do not surprise customers with discounts or surcharges.
/// </summary>
public sealed class BookingPricingOptions
{
    public const string SectionName = "BookingPricing";

    public EarlyBirdOptions EarlyBird { get; init; } = new();
    public VipOptions Vip { get; init; } = new();
    public GroupDiscountOptions GroupDiscount { get; init; } = new();
}

public sealed class EarlyBirdOptions
{
    public bool Enabled { get; init; } = true;

    /// <summary>Buy at least this many days before the event to qualify.</summary>
    public int DaysBeforeEventStart { get; init; } = 30;

    /// <summary>Percentage off the running amount. Range 0-100.</summary>
    public decimal DiscountPercent { get; init; } = 10m;
}

public sealed class VipOptions
{
    public bool Enabled { get; init; } = true;

    /// <summary>Metadata key inspected on the pricing context.</summary>
    public string TierMetadataKey { get; init; } = "customerTier";

    /// <summary>Tier value that triggers the surcharge (case-insensitive match).</summary>
    public string TierValue { get; init; } = "vip";

    /// <summary>Percentage added on top of the running amount. Range 0-100.</summary>
    public decimal SurchargePercent { get; init; } = 20m;
}

public sealed class GroupDiscountOptions
{
    public bool Enabled { get; init; } = true;

    /// <summary>Minimum seat count in a booking to qualify.</summary>
    public int MinimumSeats { get; init; } = 5;

    /// <summary>Percentage off the running amount. Range 0-100.</summary>
    public decimal DiscountPercent { get; init; } = 15m;
}
