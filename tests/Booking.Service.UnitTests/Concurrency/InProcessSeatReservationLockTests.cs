using Booking.Service.Domain.ValueObjects;
using Booking.Service.Infrastructure.Concurrency;
using FluentAssertions;

namespace Booking.Service.UnitTests.Concurrency;

public sealed class InProcessSeatReservationLockTests
{
    private static readonly EventId TestEvent = EventId.New();
    private static readonly SeatNumber SeatA1 = SeatNumber.Create("A1").Value;
    private static readonly SeatNumber SeatA2 = SeatNumber.Create("A2").Value;

    [Fact]
    public async Task TryAcquire_Should_Succeed_When_Free()
    {
        var sut = new InProcessSeatReservationLock();

        await using var handle = await sut.TryAcquireAsync(TestEvent, SeatA1, CancellationToken.None);

        handle.Should().NotBeNull();
    }

    [Fact]
    public async Task TryAcquire_Should_Return_Null_When_Same_Seat_Already_Held()
    {
        var sut = new InProcessSeatReservationLock();
        await using var first = await sut.TryAcquireAsync(TestEvent, SeatA1, CancellationToken.None);
        first.Should().NotBeNull();

        var second = await sut.TryAcquireAsync(TestEvent, SeatA1, CancellationToken.None);

        second.Should().BeNull();
    }

    [Fact]
    public async Task TryAcquire_Should_Succeed_For_Different_Seat_On_Same_Event()
    {
        var sut = new InProcessSeatReservationLock();
        await using var seatAHandle = await sut.TryAcquireAsync(TestEvent, SeatA1, CancellationToken.None);
        seatAHandle.Should().NotBeNull();

        await using var seatBHandle = await sut.TryAcquireAsync(TestEvent, SeatA2, CancellationToken.None);

        seatBHandle.Should().NotBeNull("different seats must not block each other");
    }

    [Fact]
    public async Task TryAcquire_Should_Succeed_For_Same_Seat_On_Different_Events()
    {
        var sut = new InProcessSeatReservationLock();
        var eventA = EventId.New();
        var eventB = EventId.New();

        await using var first = await sut.TryAcquireAsync(eventA, SeatA1, CancellationToken.None);
        await using var second = await sut.TryAcquireAsync(eventB, SeatA1, CancellationToken.None);

        first.Should().NotBeNull();
        second.Should().NotBeNull("seat keys must include the event id");
    }

    [Fact]
    public async Task TryAcquire_Should_Reacquire_After_Release()
    {
        var sut = new InProcessSeatReservationLock();

        var first = await sut.TryAcquireAsync(TestEvent, SeatA1, CancellationToken.None);
        first.Should().NotBeNull();
        await first!.DisposeAsync();

        await using var second = await sut.TryAcquireAsync(TestEvent, SeatA1, CancellationToken.None);
        second.Should().NotBeNull();
    }

    [Fact]
    public async Task TryAcquire_Should_Allow_Exactly_One_Concurrent_Holder_Per_Seat()
    {
        var sut = new InProcessSeatReservationLock();
        const int contenders = 50;

        var handles = await Task.WhenAll(Enumerable.Range(0, contenders)
            .Select(_ => sut.TryAcquireAsync(TestEvent, SeatA1, CancellationToken.None)));

        handles.Count(handle => handle is not null).Should().Be(1);
    }
}
