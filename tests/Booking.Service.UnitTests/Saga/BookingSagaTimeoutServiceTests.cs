using Booking.Service.Infrastructure.Configuration;
using Booking.Service.Infrastructure.Persistence;
using Booking.Service.Infrastructure.Persistence.Models;
using Booking.Service.Infrastructure.Saga;
using BuildingBlocks.Abstractions.Messaging;
using BuildingBlocks.Abstractions.Observability;
using BuildingBlocks.Observability;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Booking.Service.UnitTests.Saga;

/// <summary>
/// Unit-tests <see cref="BookingSagaTimeoutService"/> in isolation. The service has three jobs:
/// (1) find sagas stuck in <c>Refunding</c> longer than the configured threshold, (2) publish a
/// <see cref="RefundTimeoutExpiredV1"/> for each, (3) leave the saga state untouched so the
/// state machine owns the transition.
/// </summary>
public sealed class BookingSagaTimeoutServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private BookingDbContext _dbContext = null!;
    private ITestHarness _harness = null!;
    private ServiceProvider _serviceProvider = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseSqlite(_connection)
            .Options;
        _dbContext = new BookingDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        var services = new ServiceCollection();
        services.AddMassTransitTestHarness();
        _serviceProvider = services.BuildServiceProvider(true);
        _harness = _serviceProvider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        await _harness.Stop();
        await _serviceProvider.DisposeAsync();
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task RunOnceAsync_Should_Publish_TimeoutEvent_For_Stale_Refunding_Saga()
    {
        var bookingId = Guid.NewGuid();
        var paymentIntentId = Guid.NewGuid();
        var refundRequestedAt = DateTime.UtcNow.AddMinutes(-5);

        await SeedSagaAsync(
            bookingId,
            currentState: "Refunding",
            refundRequestedAtUtc: refundRequestedAt,
            paymentIntentId: paymentIntentId);

        var service = BuildService(refundTimeoutSeconds: 60);

        var published = await service.RunOnceAsync(CancellationToken.None);

        published.Should().Be(1);

        // The contract published must match what the state machine subscribes to, with the saga
        // bookingId / payment intent id propagated for ops triage.
        (await _harness.Published.Any<RefundTimeoutExpiredV1>(x =>
            x.Context.Message.BookingId == bookingId
            && x.Context.Message.PaymentIntentId == paymentIntentId
            && x.Context.Message.ElapsedSeconds >= 60)).Should().BeTrue();
    }

    [Fact]
    public async Task RunOnceAsync_Should_Skip_Sagas_Within_Timeout_Window()
    {
        var bookingId = Guid.NewGuid();
        await SeedSagaAsync(
            bookingId,
            currentState: "Refunding",
            refundRequestedAtUtc: DateTime.UtcNow.AddSeconds(-10), // well under 60s threshold
            paymentIntentId: Guid.NewGuid());

        var service = BuildService(refundTimeoutSeconds: 60);

        var published = await service.RunOnceAsync(CancellationToken.None);

        published.Should().Be(0);
        (await _harness.Published.Any<RefundTimeoutExpiredV1>()).Should().BeFalse(
            "young sagas must not be escalated — that would cause a refund storm during a Payment outage recovery");
    }

    [Fact]
    public async Task RunOnceAsync_Should_Skip_Sagas_In_Other_States()
    {
        // AwaitingNotification + RefundRequestedAtUtc=null should NOT be touched. The worker is
        // narrowly scoped to Refunding so it cannot misclassify happy-path sagas as escalations.
        var bookingId = Guid.NewGuid();
        await SeedSagaAsync(
            bookingId,
            currentState: "AwaitingNotification",
            refundRequestedAtUtc: null,
            paymentIntentId: Guid.NewGuid());

        var service = BuildService(refundTimeoutSeconds: 60);

        var published = await service.RunOnceAsync(CancellationToken.None);

        published.Should().Be(0);
    }

    [Fact]
    public async Task RunOnceAsync_Should_Respect_BatchSize_Cap()
    {
        // Three stale sagas, batch size of 2 — only two should be published this sweep. The third
        // is left for the next sweep so a Payment.Service outage that strands thousands of sagas
        // cannot produce a single-message-burst that overwhelms RabbitMQ.
        for (var i = 0; i < 3; i++)
        {
            await SeedSagaAsync(
                Guid.NewGuid(),
                currentState: "Refunding",
                refundRequestedAtUtc: DateTime.UtcNow.AddMinutes(-5),
                paymentIntentId: Guid.NewGuid());
        }

        var service = BuildService(refundTimeoutSeconds: 60, batchSize: 2);

        var published = await service.RunOnceAsync(CancellationToken.None);

        published.Should().Be(2);
    }

    [Fact]
    public async Task RunOnceAsync_Should_Not_Mutate_Saga_State_Directly()
    {
        // State machine owns the transition. The service only publishes the event; the saga row
        // must stay as the machine left it (any change here would race the machine's own update).
        var bookingId = Guid.NewGuid();
        await SeedSagaAsync(
            bookingId,
            currentState: "Refunding",
            refundRequestedAtUtc: DateTime.UtcNow.AddMinutes(-5),
            paymentIntentId: Guid.NewGuid());

        var service = BuildService(refundTimeoutSeconds: 60);

        await service.RunOnceAsync(CancellationToken.None);

        // Re-read the saga state on a fresh context to bypass change tracking.
        var refreshed = await ReloadSagaAsync(bookingId);
        refreshed.Should().NotBeNull();
        refreshed!.CurrentState.Should().Be("Refunding", "the service must not transition the saga; only the state machine may");
        refreshed.RefundStatus.Should().BeNull();
        refreshed.RefundTimeoutElapsedSeconds.Should().BeNull();
    }

    private BookingSagaTimeoutService BuildService(int refundTimeoutSeconds, int batchSize = 100)
    {
        return new BookingSagaTimeoutService(
            _dbContext,
            _harness.Bus,
            Options.Create(new BookingSagaTimeoutOptions
            {
                Enabled = true,
                RefundTimeoutSeconds = refundTimeoutSeconds,
                BatchSize = batchSize
            }),
            TimeProvider.System,
            new CorrelationContextAccessor(),
            NullLogger<BookingSagaTimeoutService>.Instance);
    }

    private async Task SeedSagaAsync(
        Guid bookingId,
        string currentState,
        DateTime? refundRequestedAtUtc,
        Guid? paymentIntentId)
    {
        _dbContext.BookingPaymentSagaStates.Add(new BookingPaymentSagaState
        {
            CorrelationId = bookingId,
            BookingId = bookingId,
            EventId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CurrentState = currentState,
            SeatNumber = "A1",
            ChargeAmount = 100m,
            ChargeCurrency = "USD",
            ChargeProvider = "stripe",
            ChargeIdempotencyKey = $"booking:{bookingId:N}:charge",
            ChargeMetadataJson = "{}",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10),
            UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            PaymentIntentId = paymentIntentId,
            RefundRequestedAtUtc = refundRequestedAtUtc
        });
        await _dbContext.SaveChangesAsync();
    }

    private async Task<BookingPaymentSagaState?> ReloadSagaAsync(Guid bookingId)
    {
        // Detach so we re-fetch from SQLite rather than read the change tracker — proves the
        // worker did not mutate state through the same context instance.
        foreach (var entry in _dbContext.ChangeTracker.Entries().ToArray())
        {
            entry.State = EntityState.Detached;
        }

        return await _dbContext.BookingPaymentSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(saga => saga.BookingId == bookingId);
    }
}
