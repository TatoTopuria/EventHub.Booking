using Booking.Service.Application.Contracts.Persistence;
using Booking.Service.Domain.Aggregates;
using Booking.Service.Domain.DomainEvents;
using Booking.Service.Domain.Enums;
using Booking.Service.Domain.ValueObjects;
using Booking.Service.Infrastructure.Configuration;
using Booking.Service.Infrastructure.Expiry;
using Booking.Service.UnitTests.Builders;
using BuildingBlocks.Time;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using BookingAggregate = Booking.Service.Domain.Aggregates.Booking;

namespace Booking.Service.UnitTests.Expiry;

public sealed class BookingExpiryServiceTests
{
    private static readonly DateTime FixedNowUtc = new(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RunOnce_Should_Skip_When_Lock_Cannot_Be_Acquired()
    {
        var (bookingRepository, eventRepository, unitOfWork) = BuildPersistence();
        var lockStub = new RejectingLock();
        var service = BuildService(bookingRepository, eventRepository, unitOfWork, lockStub);

        var result = await service.RunOnceAsync(CancellationToken.None);

        result.Skipped.Should().BeTrue();
        result.Expired.Should().Be(0);
        unitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task RunOnce_Should_Return_Zero_When_No_Bookings_Are_Due()
    {
        var (bookingRepository, eventRepository, unitOfWork) = BuildPersistence();
        var service = BuildService(bookingRepository, eventRepository, unitOfWork, new GrantingLock());

        var result = await service.RunOnceAsync(CancellationToken.None);

        result.Skipped.Should().BeFalse();
        result.Expired.Should().Be(0);
    }

    [Fact]
    public async Task RunOnce_Should_Expire_Due_Booking_And_Release_Seats_On_Event()
    {
        var (bookingRepository, eventRepository, unitOfWork) = BuildPersistence();

        var eventAggregate = new EventBuilder().Published().Build();
        var seat = eventAggregate.Seats.First();
        eventAggregate.MarkSeatReserved(seat);
        await eventRepository.AddAsync(eventAggregate, CancellationToken.None);

        var createdAtUtc = FixedNowUtc.AddMinutes(-15);
        var booking = BookingAggregate.Create(Guid.NewGuid(), eventAggregate.Id, CustomerId.New(), createdAtUtc).Value;
        booking.ReserveSeat(seat, createdAtUtc);
        booking.ClearDomainEvents();
        await bookingRepository.AddAsync(booking, CancellationToken.None);

        var service = BuildService(bookingRepository, eventRepository, unitOfWork, new GrantingLock());

        var result = await service.RunOnceAsync(CancellationToken.None);

        result.Expired.Should().Be(1);
        result.Failed.Should().Be(0);

        var refreshed = await bookingRepository.GetByIdAsync(booking.Id, CancellationToken.None);
        refreshed!.Status.Should().Be(BookingStatus.Expired);

        var refreshedEvent = await eventRepository.GetByIdAsync(eventAggregate.Id, CancellationToken.None);
        refreshedEvent!.ReservedSeats.Should().NotContain(seat);

        // Outbox plumbing is exercised via SaveChangesAsync — confirm domain events fired.
        refreshed.DomainEvents.OfType<BookingExpired>().Should().HaveCount(1);
        refreshed.DomainEvents.OfType<SeatReleased>().Should().HaveCount(1);
        unitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task RunOnce_Should_Not_Touch_Bookings_That_Are_Below_The_Threshold()
    {
        var (bookingRepository, eventRepository, unitOfWork) = BuildPersistence();

        var eventAggregate = new EventBuilder().Published().Build();
        var seat = eventAggregate.Seats.First();
        eventAggregate.MarkSeatReserved(seat);
        await eventRepository.AddAsync(eventAggregate, CancellationToken.None);

        var createdAtUtc = FixedNowUtc.AddMinutes(-5); // half the expiry window
        var booking = BookingAggregate.Create(Guid.NewGuid(), eventAggregate.Id, CustomerId.New(), createdAtUtc).Value;
        booking.ReserveSeat(seat, createdAtUtc);
        booking.ClearDomainEvents();
        await bookingRepository.AddAsync(booking, CancellationToken.None);

        var service = BuildService(bookingRepository, eventRepository, unitOfWork, new GrantingLock());

        var result = await service.RunOnceAsync(CancellationToken.None);

        result.Expired.Should().Be(0);
        var refreshed = await bookingRepository.GetByIdAsync(booking.Id, CancellationToken.None);
        refreshed!.Status.Should().Be(BookingStatus.PendingPayment);
    }

    private static BookingExpiryService BuildService(
        IBookingRepository bookingRepository,
        IEventRepository eventRepository,
        FakeBookingUnitOfWork unitOfWork,
        IBookingExpiryLock expiryLock)
    {
        var options = Options.Create(new BookingExpiryOptions
        {
            Enabled = true,
            ExpiryAfterMinutes = 10,
            BatchSize = 50,
            ScanIntervalSeconds = 30
        });

        return new BookingExpiryService(
            bookingRepository,
            eventRepository,
            unitOfWork,
            expiryLock,
            new FixedClock(FixedNowUtc),
            options,
            NullLogger<BookingExpiryService>.Instance);
    }

    private static (FakeBookingRepository bookings, FakeEventRepository events, FakeBookingUnitOfWork unitOfWork) BuildPersistence()
    {
        return (new FakeBookingRepository(), new FakeEventRepository(), new FakeBookingUnitOfWork());
    }

    private sealed class FixedClock(DateTime nowUtc) : IClockProvider
    {
        public DateTimeOffset GetUtcNow() => new(nowUtc, TimeSpan.Zero);
    }

    /// <summary>
    /// Test-only in-memory <see cref="IBookingRepository"/>. Deliberately stores aggregates by
    /// reference (no serialization round-trip) so the assertions below can observe
    /// <c>DomainEvents</c> accumulated by <see cref="BookingExpiryService"/>. An EF-backed Ef*
    /// repository would drain events into <c>DomainEventCollector</c> on each Update and the
    /// reference-equality assertion would silently degrade to "empty events". Kept private to the
    /// test class so this shortcut never leaks into production composition — that was the dead
    /// code F6/G7 removed from Infrastructure.
    /// </summary>
    private sealed class FakeBookingRepository : IBookingRepository
    {
        private readonly Dictionary<Guid, BookingAggregate> _storage = new();

        public Task AddAsync(BookingAggregate booking, CancellationToken cancellationToken = default)
        {
            _storage[booking.Id] = booking;
            return Task.CompletedTask;
        }

        public Task<BookingAggregate?> GetByIdAsync(Guid bookingId, CancellationToken cancellationToken = default)
        {
            _storage.TryGetValue(bookingId, out var booking);
            return Task.FromResult(booking);
        }

        public Task UpdateAsync(BookingAggregate booking, CancellationToken cancellationToken = default)
        {
            _storage[booking.Id] = booking;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<BookingAggregate>> GetByCustomerIdAsync(
            CustomerId customerId,
            DateTime? lastCreatedAtUtc,
            Guid? lastBookingId,
            int take,
            CancellationToken cancellationToken = default)
        {
            // Not exercised by these tests; kept compilable so the interface contract is satisfied.
            return Task.FromResult<IReadOnlyCollection<BookingAggregate>>(Array.Empty<BookingAggregate>());
        }

        public Task<IReadOnlyCollection<BookingAggregate>> GetExpiringPendingAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            var items = _storage.Values
                .Where(booking => booking.Status == BookingStatus.PendingPayment && booking.CreatedAtUtc < cutoffUtc)
                .OrderBy(booking => booking.CreatedAtUtc)
                .ThenBy(booking => booking.Id)
                .Take(Math.Clamp(batchSize, 1, 500))
                .ToArray();

            return Task.FromResult<IReadOnlyCollection<BookingAggregate>>(items);
        }
    }

    /// <summary>
    /// Test-only in-memory <see cref="IEventRepository"/>. Same rationale as
    /// <see cref="FakeBookingRepository"/> — by-reference storage keeps domain-event assertions
    /// honest without dragging in a SQLite round-trip.
    /// </summary>
    private sealed class FakeEventRepository : IEventRepository
    {
        private readonly Dictionary<Guid, Event> _storage = new();

        public Task AddAsync(Event eventAggregate, CancellationToken cancellationToken = default)
        {
            _storage[eventAggregate.Id.Value] = eventAggregate;
            return Task.CompletedTask;
        }

        public Task<Event?> GetByIdAsync(EventId eventId, CancellationToken cancellationToken = default)
        {
            _storage.TryGetValue(eventId.Value, out var eventAggregate);
            return Task.FromResult(eventAggregate);
        }

        public Task UpdateAsync(Event eventAggregate, CancellationToken cancellationToken = default)
        {
            _storage[eventAggregate.Id.Value] = eventAggregate;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<Event>> ListAsync(
            DateTime? lastStartsAtUtc,
            Guid? lastEventId,
            int take,
            CancellationToken cancellationToken = default)
        {
            // Not exercised by these tests; kept compilable so the interface contract is satisfied.
            return Task.FromResult<IReadOnlyCollection<Event>>(Array.Empty<Event>());
        }
    }

    private sealed class GrantingLock : IBookingExpiryLock
    {
        public Task<IBookingExpiryLockHandle?> TryAcquireAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IBookingExpiryLockHandle?>(new Handle());

        private sealed class Handle : IBookingExpiryLockHandle
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class RejectingLock : IBookingExpiryLock
    {
        public Task<IBookingExpiryLockHandle?> TryAcquireAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IBookingExpiryLockHandle?>(null);
    }

    private sealed class FakeBookingUnitOfWork : IBookingUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }
}
