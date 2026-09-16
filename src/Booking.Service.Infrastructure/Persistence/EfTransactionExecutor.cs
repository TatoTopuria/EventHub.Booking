using Booking.Service.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Booking.Service.Infrastructure.Persistence;

public sealed class EfTransactionExecutor(BookingDbContext dbContext) : ITransactionExecutor
{
    public async Task<TResponse> ExecuteAsync<TResponse>(
        Func<CancellationToken, Task<TResponse>> operation,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var response = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
