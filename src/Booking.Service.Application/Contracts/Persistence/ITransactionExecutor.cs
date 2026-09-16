namespace Booking.Service.Application.Contracts.Persistence;

/// <summary>
/// Executes command work inside an explicit database transaction.
/// </summary>
public interface ITransactionExecutor
{
    Task<TResponse> ExecuteAsync<TResponse>(
        Func<CancellationToken, Task<TResponse>> operation,
        CancellationToken cancellationToken = default);
}
