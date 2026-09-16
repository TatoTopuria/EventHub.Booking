using Booking.Service.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Booking.Service.Infrastructure.Expiry;

/// <summary>
/// Redis-backed implementation of <see cref="IBookingExpiryLock"/> using <c>SET key value NX EX ttl</c>.
/// </summary>
/// <remarks>
/// Each acquisition writes a unique owner token; release performs a token-compare delete so we never
/// release a lock the lease of which has already expired and been reacquired by another replica.
/// </remarks>
public sealed class RedisBookingExpiryLock : IBookingExpiryLock
{
    // Lua script implementing CAS-delete: only delete the key when its value matches our owner token.
    // Keeps replicas honest if their previous lease quietly expired while they were still running.
    private const string ReleaseScript = @"
if redis.call('GET', KEYS[1]) == ARGV[1] then
    return redis.call('DEL', KEYS[1])
else
    return 0
end";

    private readonly IConnectionMultiplexer _connection;
    private readonly BookingExpiryLockOptions _options;
    private readonly ILogger<RedisBookingExpiryLock> _logger;

    public RedisBookingExpiryLock(
        IConnectionMultiplexer connection,
        IOptions<BookingExpiryOptions> options,
        ILogger<RedisBookingExpiryLock> logger)
    {
        _connection = connection;
        _options = options.Value.Lock;
        _logger = logger;
    }

    public async Task<IBookingExpiryLockHandle?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var database = _connection.GetDatabase();
        var owner = $"{Environment.MachineName}:{Guid.NewGuid():N}";
        var ttl = TimeSpan.FromSeconds(Math.Max(1, _options.TtlSeconds));

        var acquired = await database.StringSetAsync(_options.Key, owner, ttl, When.NotExists);
        if (!acquired)
        {
            return null;
        }

        return new RedisLockHandle(database, _options.Key, owner, _logger);
    }

    private sealed class RedisLockHandle(
        IDatabase database,
        string key,
        string owner,
        ILogger<RedisBookingExpiryLock> logger) : IBookingExpiryLockHandle
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await database.ScriptEvaluateAsync(
                    ReleaseScript,
                    new RedisKey[] { key },
                    new RedisValue[] { owner });
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "BookingExpiryLockReleaseFailed key={Key} owner={Owner} message={Message}",
                    key,
                    owner,
                    exception.Message);
            }
        }
    }
}
