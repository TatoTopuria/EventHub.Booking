using Booking.Service.Application.Contracts.Persistence;
using Booking.Service.Infrastructure.Messaging;
using Booking.Service.Infrastructure.Persistence;
using Booking.Service.Infrastructure.Persistence.Models;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Booking.Service.UnitTests;

public sealed class EfUnitOfWorkTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly BookingDbContext _dbContext;
    private readonly IBookingUnitOfWork _unitOfWork;
    private readonly EfTransactionExecutor _transactionExecutor;

    public EfUnitOfWorkTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new BookingDbContext(options);
        _unitOfWork = new EfUnitOfWork(_dbContext, new DomainEventCollector(), new HttpContextAccessor());
        _transactionExecutor = new EfTransactionExecutor(_dbContext);
    }

    [Fact]
    public async Task SaveChangesAsync_Should_Commit_Pending_Changes()
    {
        _dbContext.Events.Add(CreateEventEntity(Guid.NewGuid(), "OrgA"));

        await _unitOfWork.SaveChangesAsync();

        var eventsCount = await _dbContext.Events.CountAsync();
        eventsCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Rollback_On_Exception()
    {
        var eventId = Guid.NewGuid();

        var action = async () =>
            await _transactionExecutor.ExecuteAsync<int>(async _ =>
            {
                _dbContext.Events.Add(CreateEventEntity(eventId, "OrgB"));
                await _dbContext.SaveChangesAsync();
                throw new InvalidOperationException("fail");
            });

        await action.Should().ThrowAsync<InvalidOperationException>();

        _dbContext.ChangeTracker.Clear();
        var exists = await _dbContext.Events.AnyAsync(entity => entity.Id == eventId);
        exists.Should().BeFalse();
    }

    public async Task InitializeAsync()
    {
        await _dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static EventEntity CreateEventEntity(Guid id, string organizer)
    {
        return new EventEntity
        {
            Id = id,
            Organizer = organizer,
            StartsAtUtc = DateTime.UtcNow,
            EndsAtUtc = DateTime.UtcNow.AddHours(2),
            Status = 1,
            Seats =
            [
                new EventSeatEntity { EventId = id, SeatNumber = "A1" }
            ],
            ReservedSeats = []
        };
    }
}
