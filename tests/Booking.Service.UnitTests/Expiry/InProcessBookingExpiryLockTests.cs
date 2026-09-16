using Booking.Service.Infrastructure.Expiry;
using FluentAssertions;

namespace Booking.Service.UnitTests.Expiry;

public sealed class InProcessBookingExpiryLockTests
{
    [Fact]
    public async Task TryAcquire_Should_Succeed_When_Lock_Is_Free()
    {
        var sut = new InProcessBookingExpiryLock();

        await using var handle = await sut.TryAcquireAsync(CancellationToken.None);

        handle.Should().NotBeNull();
    }

    [Fact]
    public async Task TryAcquire_Should_Return_Null_When_Lock_Is_Held()
    {
        var sut = new InProcessBookingExpiryLock();
        await using var firstHandle = await sut.TryAcquireAsync(CancellationToken.None);
        firstHandle.Should().NotBeNull();

        var secondAttempt = await sut.TryAcquireAsync(CancellationToken.None);

        secondAttempt.Should().BeNull();
    }

    [Fact]
    public async Task TryAcquire_Should_Succeed_Again_After_Release()
    {
        var sut = new InProcessBookingExpiryLock();

        var first = await sut.TryAcquireAsync(CancellationToken.None);
        first.Should().NotBeNull();
        await first!.DisposeAsync();

        await using var second = await sut.TryAcquireAsync(CancellationToken.None);
        second.Should().NotBeNull();
    }

    [Fact]
    public async Task TryAcquire_Should_Allow_Exactly_One_Concurrent_Holder()
    {
        var sut = new InProcessBookingExpiryLock();
        const int contenders = 32;

        var handles = await Task.WhenAll(Enumerable.Range(0, contenders)
            .Select(_ => sut.TryAcquireAsync(CancellationToken.None)));

        handles.Count(handle => handle is not null).Should().Be(1);
    }
}
