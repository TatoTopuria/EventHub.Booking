namespace Booking.Service.Infrastructure.Configuration;

public sealed class BookingSagaPaymentOptions
{
    public const string SectionName = "BookingSagaPayment";

    public decimal DefaultAmount { get; init; } = 100m;

    public string DefaultCurrency { get; init; } = "USD";

    public string DefaultProvider { get; init; } = "stripe";

    public Dictionary<string, string> DefaultMetadata { get; init; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cardNumber"] = "4242 4242 4242 4242"
    };

    public Dictionary<string, BookingSagaEventPaymentOption> EventPrices { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class BookingSagaEventPaymentOption
{
    public decimal Amount { get; init; }

    public string Currency { get; init; } = "USD";

    public string Provider { get; init; } = "stripe";

    /// <summary>
    /// Optional event start time used by time-based pricing strategies (e.g. early bird).
    /// When null, time-based strategies fall back to a sentinel that effectively disables them.
    /// </summary>
    public DateTimeOffset? StartsAtUtc { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}