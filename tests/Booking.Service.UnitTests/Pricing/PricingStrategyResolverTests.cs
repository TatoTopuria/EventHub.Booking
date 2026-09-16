using Booking.Service.Application.Pricing;
using Booking.Service.Domain.ValueObjects;
using FluentAssertions;

namespace Booking.Service.UnitTests.Pricing;

public sealed class PricingStrategyResolverTests
{
    [Fact]
    public void Resolve_Should_Apply_Strategies_In_Priority_Order()
    {
        var sequence = new List<string>();
        var strategies = new IPricingStrategy[]
        {
            new RecordingStrategy("Third", priority: 300, sequence),
            new RecordingStrategy("First", priority: 100, sequence),
            new RecordingStrategy("Second", priority: 200, sequence)
        };

        var resolver = new PricingStrategyResolver(strategies);
        resolver.Resolve(BuildContext(Money.Create(100m, "USD").Value));

        sequence.Should().Equal("First", "Second", "Third");
    }

    [Fact]
    public void Resolve_Should_Skip_Strategies_That_Leave_Amount_Unchanged_In_Audit_Trail()
    {
        var strategies = new IPricingStrategy[]
        {
            new MultiplyStrategy("Discount", priority: 100, multiplier: 0.9m),
            new IdentityStrategy("NoOp", priority: 200),
            new MultiplyStrategy("Surcharge", priority: 300, multiplier: 1.1m)
        };

        var resolver = new PricingStrategyResolver(strategies);
        var decision = resolver.Resolve(BuildContext(Money.Create(100m, "USD").Value));

        decision.FinalAmount.Amount.Should().Be(100m * 0.9m * 1.1m);
        decision.Adjustments.Select(a => a.StrategyName).Should().Equal("Discount", "Surcharge");
    }

    [Fact]
    public void Resolve_Should_Return_Base_Amount_When_No_Strategy_Changes_It()
    {
        var strategies = new IPricingStrategy[]
        {
            new IdentityStrategy("Identity1", priority: 100),
            new IdentityStrategy("Identity2", priority: 200)
        };

        var resolver = new PricingStrategyResolver(strategies);
        var baseAmount = Money.Create(75m, "EUR").Value;

        var decision = resolver.Resolve(BuildContext(baseAmount));

        decision.FinalAmount.Should().Be(baseAmount);
        decision.Adjustments.Should().BeEmpty();
    }

    private static BookingPricingContext BuildContext(Money baseAmount) => new(
        EventId: EventId.New(),
        CustomerId: CustomerId.New(),
        EventStartsAtUtc: DateTimeOffset.UtcNow.AddDays(60),
        BaseAmount: baseAmount,
        SeatCount: 1,
        Metadata: new Dictionary<string, string>());

    private sealed class RecordingStrategy(string name, int priority, List<string> sequence) : IPricingStrategy
    {
        public int Priority => priority;
        public string Name => name;
        public Money Apply(BookingPricingContext context, Money currentAmount)
        {
            sequence.Add(name);
            return currentAmount;
        }
    }

    private sealed class IdentityStrategy(string name, int priority) : IPricingStrategy
    {
        public int Priority => priority;
        public string Name => name;
        public Money Apply(BookingPricingContext context, Money currentAmount) => currentAmount;
    }

    private sealed class MultiplyStrategy(string name, int priority, decimal multiplier) : IPricingStrategy
    {
        public int Priority => priority;
        public string Name => name;
        public Money Apply(BookingPricingContext context, Money currentAmount)
        {
            var next = Money.Create(currentAmount.Amount * multiplier, currentAmount.Currency);
            return next.IsSuccess ? next.Value : currentAmount;
        }
    }
}
