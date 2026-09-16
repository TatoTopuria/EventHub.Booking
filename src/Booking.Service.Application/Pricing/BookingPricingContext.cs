using Booking.Service.Domain.ValueObjects;

namespace Booking.Service.Application.Pricing;

/// <summary>
/// Input bundle passed to every <see cref="IPricingStrategy"/>. Carries the base amount plus
/// enough contextual hints for strategies to decide whether they apply.
/// </summary>
/// <param name="EventId">The event being booked.</param>
/// <param name="CustomerId">The customer placing the booking.</param>
/// <param name="EventStartsAtUtc">Used by time-based strategies (e.g. early bird).</param>
/// <param name="BaseAmount">The price before any adjustments.</param>
/// <param name="SeatCount">How many seats this customer is purchasing in the current booking.</param>
/// <param name="Metadata">
/// Opaque key/value bag. Strategies use it to look up customer tier, seat tier, promo codes, etc.,
/// keeping the contract stable as new pricing dimensions are added.
/// </param>
public sealed record BookingPricingContext(
    EventId EventId,
    CustomerId CustomerId,
    DateTimeOffset EventStartsAtUtc,
    Money BaseAmount,
    int SeatCount,
    IReadOnlyDictionary<string, string> Metadata);
