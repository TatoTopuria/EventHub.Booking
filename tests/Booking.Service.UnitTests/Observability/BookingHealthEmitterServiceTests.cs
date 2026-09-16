using Booking.Service.Infrastructure.Configuration;
using Booking.Service.Infrastructure.Observability;
using Booking.Service.Infrastructure.Persistence;
using Booking.Service.Infrastructure.Persistence.Models;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Booking.Service.UnitTests.Observability;

/// <summary>
/// Pins the structured-log shape and per-state breakdown emitted by
/// <see cref="BookingHealthEmitterService"/>. Kibana dashboards filter on the exact event names
/// and property keys below — any rename here must be matched in the saved-objects bundle.
/// </summary>
public sealed class BookingHealthEmitterServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private BookingDbContext _dbContext = null!;
    private CapturingLogger<BookingHealthEmitterService> _logger = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseSqlite(_connection)
            .Options;
        _dbContext = new BookingDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        _logger = new CapturingLogger<BookingHealthEmitterService>();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Emit_Should_Produce_One_SagaProgress_Line_Per_State()
    {
        SeedSaga("AwaitingPayment");
        SeedSaga("AwaitingPayment");
        SeedSaga("Refunding");
        await _dbContext.SaveChangesAsync();

        var service = BuildService();
        await service.EmitOnceAsync(CancellationToken.None);

        var sagaLines = _logger.Entries.Where(e => e.Message.StartsWith("EventHubSagaProgress", StringComparison.Ordinal)).ToList();

        sagaLines.Should().HaveCount(2, "one log line per distinct CurrentState the dashboard renders");
        sagaLines.Should().Contain(line =>
            line.PropertyValue<string>("State") == "AwaitingPayment"
            && line.PropertyValue<long>("Count") == 2L);
        sagaLines.Should().Contain(line =>
            line.PropertyValue<string>("State") == "Refunding"
            && line.PropertyValue<long>("Count") == 1L);
    }

    [Fact]
    public async Task Emit_Should_Produce_Baseline_SagaProgress_When_Table_Empty()
    {
        // Empty saga table still emits ONE EventHubSagaProgress line with count=0 so a Kibana
        // panel filtered on the event name always has data points. Absent data on the dashboard
        // is harder to diagnose than explicit zero.
        var service = BuildService();
        await service.EmitOnceAsync(CancellationToken.None);

        var sagaLines = _logger.Entries.Where(e => e.Message.StartsWith("EventHubSagaProgress", StringComparison.Ordinal)).ToList();
        sagaLines.Should().ContainSingle();
        sagaLines.Single().PropertyValue<long>("Count").Should().Be(0L);
    }

    [Fact]
    public async Task Emit_Should_Split_Outbox_Backlog_Into_Total_And_Stuck()
    {
        // Three pending rows: one young (1 s old), one stuck (3 min old), one stuck (5 min old).
        // With OutboxStuckAfterSeconds=60, stuckTotal must equal 2 and pendingTotal 3.
        var nowUtc = DateTime.UtcNow;
        SeedOutbox(processed: false, occurredOn: nowUtc.AddSeconds(-1));
        SeedOutbox(processed: false, occurredOn: nowUtc.AddMinutes(-3));
        SeedOutbox(processed: false, occurredOn: nowUtc.AddMinutes(-5));
        SeedOutbox(processed: true, occurredOn: nowUtc.AddMinutes(-10)); // processed — should not count
        await _dbContext.SaveChangesAsync();

        var service = BuildService(outboxStuckAfterSeconds: 60);
        await service.EmitOnceAsync(CancellationToken.None);

        var lagLine = _logger.Entries.Single(e => e.Message.StartsWith("EventHubOutboxLag", StringComparison.Ordinal));

        lagLine.PropertyValue<long>("PendingTotal").Should().Be(3L);
        lagLine.PropertyValue<long>("StuckTotal").Should().Be(2L);
        lagLine.PropertyValue<int>("StuckAfterSeconds").Should().Be(60);
        lagLine.PropertyValue<long>("OldestPendingAgeSeconds").Should().BeGreaterThanOrEqualTo(300L,
            "oldest pending row is 5 minutes old");
    }

    [Fact]
    public async Task Emit_Should_Report_Zero_OldestAge_When_Outbox_Empty()
    {
        var service = BuildService();
        await service.EmitOnceAsync(CancellationToken.None);

        var lagLine = _logger.Entries.Single(e => e.Message.StartsWith("EventHubOutboxLag", StringComparison.Ordinal));
        lagLine.PropertyValue<long>("PendingTotal").Should().Be(0L);
        lagLine.PropertyValue<long>("StuckTotal").Should().Be(0L);
        lagLine.PropertyValue<long>("OldestPendingAgeSeconds").Should().Be(0L);
    }

    private BookingHealthEmitterService BuildService(int outboxStuckAfterSeconds = 60)
    {
        return new BookingHealthEmitterService(
            _dbContext,
            Options.Create(new BookingHealthEmitterOptions
            {
                Enabled = true,
                OutboxStuckAfterSeconds = outboxStuckAfterSeconds
            }),
            TimeProvider.System,
            _logger);
    }

    private void SeedSaga(string currentState)
    {
        var bookingId = Guid.NewGuid();
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
            UpdatedAtUtc = DateTime.UtcNow
        });
    }

    private void SeedOutbox(bool processed, DateTime occurredOn)
    {
        _dbContext.OutboxMessages.Add(new OutboxMessageEntity
        {
            Id = Guid.NewGuid(),
            Type = "TestEvent",
            RoutingKey = "test",
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredOnUtc = occurredOn,
            Payload = "{}",
            ProcessedOnUtc = processed ? DateTime.UtcNow : null
        });
    }

    /// <summary>
    /// Minimal ILogger that captures every log call as a structured record so tests can assert on
    /// the message template AND the named-property values. Mirrors the assertion pattern that
    /// Kibana itself does on the indexed log line.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            var props = new Dictionary<string, object?>(StringComparer.Ordinal);

            if (state is IEnumerable<KeyValuePair<string, object?>> kvps)
            {
                foreach (var kvp in kvps)
                {
                    props[kvp.Key] = kvp.Value;
                }
            }

            Entries.Add(new LogEntry(logLevel, message, props));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Properties)
    {
        public TValue PropertyValue<TValue>(string name)
        {
            Properties.Should().ContainKey(name);
            var raw = Properties[name];
            raw.Should().NotBeNull();
            return (TValue)Convert.ChangeType(raw!, typeof(TValue), System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
