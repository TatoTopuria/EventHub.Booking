using Booking.Service.Application.Pricing;
using Booking.Service.Domain.ValueObjects;
using Booking.Service.Infrastructure.Pricing;
using BuildingBlocks.Time;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Booking.Service.UnitTests.Pricing;

public sealed class ConcretePricingStrategyTests
{
    private static readonly EventId SampleEvent = EventId.New();
    private static readonly CustomerId SampleCustomer = CustomerId.New();

    [Fact]
    public void EarlyBird_Should_Discount_When_Booking_Falls_Inside_Window()
    {
        var eventStart = new DateTimeOffset(2030, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var now = eventStart.AddDays(-45); // outside default 30-day window — wait, this IS outside.
        var clock = new FakeClockProvider(now);

        var strategy = new EarlyBirdPricingStrategy(clock, BuildOptions(new BookingPricingOptions
        {
            EarlyBird = new EarlyBirdOptions { Enabled = true, DaysBeforeEventStart = 30, DiscountPercent = 10m }
        }));

        var result = strategy.Apply(BuildContext(eventStart), Money.Create(200m, "USD").Value);

        result.Amount.Should().Be(180m);
        result.Currency.Should().Be("USD");
    }

    [Fact]
    public void EarlyBird_Should_Not_Apply_When_Booking_Falls_Outside_Window()
    {
        var eventStart = new DateTimeOffset(2030, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var now = eventStart.AddDays(-10);
        var clock = new FakeClockProvider(now);

        var strategy = new EarlyBirdPricingStrategy(clock, BuildOptions(new BookingPricingOptions
        {
            EarlyBird = new EarlyBirdOptions { Enabled = true, DaysBeforeEventStart = 30, DiscountPercent = 10m }
        }));

        var result = strategy.Apply(BuildContext(eventStart), Money.Create(200m, "USD").Value);

        result.Amount.Should().Be(200m);
    }

    [Fact]
    public void EarlyBird_Should_Not_Apply_When_Disabled()
    {
        var clock = new FakeClockProvider(DateTimeOffset.UtcNow);
        var strategy = new EarlyBirdPricingStrategy(clock, BuildOptions(new BookingPricingOptions
        {
            EarlyBird = new EarlyBirdOptions { Enabled = false, DiscountPercent = 50m }
        }));

        var result = strategy.Apply(BuildContext(DateTimeOffset.MaxValue), Money.Create(200m, "USD").Value);

        result.Amount.Should().Be(200m);
    }

    [Fact]
    public void GroupDiscount_Should_Apply_When_Seat_Count_Meets_Threshold()
    {
        var strategy = new GroupDiscountPricingStrategy(BuildOptions(new BookingPricingOptions
        {
            GroupDiscount = new GroupDiscountOptions { Enabled = true, MinimumSeats = 5, DiscountPercent = 15m }
        }));

        var ctx = BuildContext(DateTimeOffset.MaxValue, seatCount: 6);
        var result = strategy.Apply(ctx, Money.Create(100m, "USD").Value);

        result.Amount.Should().Be(85m);
    }

    [Fact]
    public void GroupDiscount_Should_Not_Apply_When_Seat_Count_Below_Threshold()
    {
        var strategy = new GroupDiscountPricingStrategy(BuildOptions(new BookingPricingOptions
        {
            GroupDiscount = new GroupDiscountOptions { Enabled = true, MinimumSeats = 5, DiscountPercent = 15m }
        }));

        var ctx = BuildContext(DateTimeOffset.MaxValue, seatCount: 1);
        var result = strategy.Apply(ctx, Money.Create(100m, "USD").Value);

        result.Amount.Should().Be(100m);
    }

    [Fact]
    public void Vip_Should_Surcharge_When_Metadata_Matches_Configured_Tier()
    {
        var strategy = new VipPricingStrategy(BuildOptions(new BookingPricingOptions
        {
            Vip = new VipOptions
            {
                Enabled = true,
                TierMetadataKey = "customerTier",
                TierValue = "vip",
                SurchargePercent = 25m
            }
        }));

        var ctx = BuildContext(DateTimeOffset.MaxValue, metadata: new Dictionary<string, string>
        {
            ["customerTier"] = "VIP"
        });

        var result = strategy.Apply(ctx, Money.Create(80m, "USD").Value);
        result.Amount.Should().Be(100m);
    }

    [Fact]
    public void Vip_Should_Not_Apply_When_Metadata_Missing()
    {
        var strategy = new VipPricingStrategy(BuildOptions(new BookingPricingOptions
        {
            Vip = new VipOptions { Enabled = true, TierMetadataKey = "customerTier", TierValue = "vip", SurchargePercent = 25m }
        }));

        var ctx = BuildContext(DateTimeOffset.MaxValue);
        var result = strategy.Apply(ctx, Money.Create(80m, "USD").Value);

        result.Amount.Should().Be(80m);
    }

    [Fact]
    public void Resolver_Should_Stack_All_Three_Strategies_In_Priority_Order()
    {
        var eventStart = new DateTimeOffset(2030, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new FakeClockProvider(eventStart.AddDays(-60));

        var options = BuildOptions(new BookingPricingOptions
        {
            EarlyBird = new EarlyBirdOptions { Enabled = true, DaysBeforeEventStart = 30, DiscountPercent = 10m },
            GroupDiscount = new GroupDiscountOptions { Enabled = true, MinimumSeats = 5, DiscountPercent = 20m },
            Vip = new VipOptions { Enabled = true, TierMetadataKey = "tier", TierValue = "vip", SurchargePercent = 50m }
        });

        IPricingStrategy[] strategies =
        [
            new EarlyBirdPricingStrategy(clock, options),
            new GroupDiscountPricingStrategy(options),
            new VipPricingStrategy(options)
        ];

        var resolver = new PricingStrategyResolver(strategies);
        var ctx = new BookingPricingContext(
            SampleEvent,
            SampleCustomer,
            eventStart,
            Money.Create(100m, "USD").Value,
            SeatCount: 5,
            Metadata: new Dictionary<string, string> { ["tier"] = "vip" });

        var decision = resolver.Resolve(ctx);

        // 100 * 0.9 (early bird) = 90; * 0.8 (group) = 72; * 1.5 (VIP) = 108
        decision.FinalAmount.Amount.Should().Be(108m);
        decision.Adjustments.Should().HaveCount(3);
        decision.Adjustments[0].StrategyName.Should().Be(EarlyBirdPricingStrategy.StrategyName);
        decision.Adjustments[1].StrategyName.Should().Be(GroupDiscountPricingStrategy.StrategyName);
        decision.Adjustments[2].StrategyName.Should().Be(VipPricingStrategy.StrategyName);
    }

    private static IOptions<BookingPricingOptions> BuildOptions(BookingPricingOptions options) =>
        Options.Create(options);

    private static BookingPricingContext BuildContext(
        DateTimeOffset eventStart,
        int seatCount = 1,
        IReadOnlyDictionary<string, string>? metadata = null) => new(
        EventId: SampleEvent,
        CustomerId: SampleCustomer,
        EventStartsAtUtc: eventStart,
        BaseAmount: Money.Create(100m, "USD").Value,
        SeatCount: seatCount,
        Metadata: metadata ?? new Dictionary<string, string>());

    private sealed class FakeClockProvider : IClockProvider
    {
        private readonly DateTimeOffset _now;
        public FakeClockProvider(DateTimeOffset now) => _now = now;
        public DateTimeOffset GetUtcNow() => _now;
    }
}
