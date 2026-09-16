using Booking.Service.Infrastructure.Persistence;
using Booking.Service.Infrastructure.Persistence.Models;
using BuildingBlocks.Abstractions.Messaging;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Booking.Service.Infrastructure.Messaging;

public sealed class BookingInboxMessageStore(BookingDbContext dbContext) : IInboxMessageStore
{
    public async Task<bool> TryBeginProcessingAsync(string messageId, CancellationToken cancellationToken)
    {
        var existing = await dbContext.InboxMessages
            .SingleOrDefaultAsync(message => message.MessageId == messageId, cancellationToken);

        if (existing is not null)
        {
            return false;
        }

        await dbContext.InboxMessages.AddAsync(new InboxMessageEntity
        {
            Id = Guid.NewGuid(),
            MessageId = messageId,
            Status = InboxMessageStatus.Processing,
            ProcessingStartedAtUtc = DateTime.UtcNow,
            ProcessedAtUtc = null
        }, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgres && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }
    }

    public async Task MarkProcessedAsync(string messageId, DateTime processedAtUtc, CancellationToken cancellationToken)
    {
        var entity = await dbContext.InboxMessages
            .SingleOrDefaultAsync(message => message.MessageId == messageId, cancellationToken)
            ?? throw new InvalidOperationException($"Inbox message '{messageId}' was not found.");

        entity.Status = InboxMessageStatus.Processed;
        entity.ProcessedAtUtc = processedAtUtc;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseAsync(string messageId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.InboxMessages
            .SingleOrDefaultAsync(message => message.MessageId == messageId, cancellationToken);

        if (entity is null || entity.Status == InboxMessageStatus.Processed)
        {
            return;
        }

        dbContext.InboxMessages.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}