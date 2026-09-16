using BuildingBlocks.Time;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Service.UnitTests.Time;

public sealed class SystemClockProviderTests
{
    [Fact]
    public void GetUtcNow_Should_Match_Wrapped_TimeProvider()
    {
        var fixedInstant = new DateTimeOffset(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);
        var fakeTimeProvider = new FrozenTimeProvider(fixedInstant);
        var clock = new SystemClockProvider(fakeTimeProvider);

        clock.GetUtcNow().Should().Be(fixedInstant);
    }

    [Fact]
    public void AddEventHubClock_Should_Register_IClockProvider_And_TimeProvider_As_Singletons()
    {
        var services = new ServiceCollection();
        services.AddEventHubClock();

        using var provider = services.BuildServiceProvider();

        var clock = provider.GetRequiredService<IClockProvider>();
        clock.Should().BeOfType<SystemClockProvider>();

        var timeProvider = provider.GetRequiredService<TimeProvider>();
        timeProvider.Should().Be(TimeProvider.System);

        // Both registrations resolved as singletons → resolving twice returns the same instance.
        provider.GetRequiredService<IClockProvider>().Should().BeSameAs(clock);
        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(timeProvider);
    }

    /// <summary>
    /// Minimal TimeProvider that always reports the same instant. Avoids pulling in
    /// Microsoft.Extensions.TimeProvider.Testing just for one test.
    /// </summary>
    private sealed class FrozenTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FrozenTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
