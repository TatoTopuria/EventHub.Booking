using Booking.Service.Api.Contracts;
using BuildingBlocks.Abstractions.Observability;
using BuildingBlocks.Grpc.Catalog;
using Grpc.Core;
using Polly;
using Polly.CircuitBreaker;
using Polly.Wrap;

namespace Booking.Service.Api.Services;

public interface ICatalogEventDetailsClient
{
    Task<CatalogEventDetailsResponse?> GetEventDetailsAsync(Guid eventId, CancellationToken cancellationToken = default);
}

public sealed class CatalogUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class CatalogEventDetailsClient(
    CatalogGrpc.CatalogGrpcClient grpcClient,
    ICorrelationContextAccessor correlationContextAccessor) : ICatalogEventDetailsClient
{
    private const string GrpcCorrelationHeaderName = "x-correlation-id";
    private readonly AsyncPolicyWrap<EventDetailsReply> _policy = BuildPolicy();

    public async Task<CatalogEventDetailsResponse?> GetEventDetailsAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        try
        {
            var correlationId = correlationContextAccessor.CorrelationId;
            var headers = new Metadata();
            if (!string.IsNullOrWhiteSpace(correlationId))
            {
                headers.Add(GrpcCorrelationHeaderName, correlationId);
            }

            var reply = await _policy.ExecuteAsync(
                async token => await grpcClient.GetEventDetailsAsync(
                    new GetEventDetailsRequest { EventId = eventId.ToString() },
                    headers: headers,
                    deadline: DateTime.UtcNow.AddSeconds(2),
                    cancellationToken: token),
                cancellationToken);

            var startsAtUtc = DateTime.TryParse(reply.StartsAtUtc, out var startsAt) ? startsAt : DateTime.MinValue;
            var endsAtUtc = DateTime.TryParse(reply.EndsAtUtc, out var endsAt) ? endsAt : DateTime.MinValue;

            return new CatalogEventDetailsResponse(
                Guid.Parse(reply.EventId),
                startsAtUtc,
                endsAtUtc,
                reply.Organizer,
                reply.Status,
                reply.Seats.ToArray(),
                reply.ReservedSeats.ToArray());
        }
        catch (RpcException rpcException) when (rpcException.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
        catch (BrokenCircuitException<EventDetailsReply> exception)
        {
            throw new CatalogUnavailableException("Catalog service circuit breaker is open.", exception);
        }
        catch (RpcException rpcException) when (IsTransient(rpcException))
        {
            throw new CatalogUnavailableException("Catalog service is temporarily unavailable.", rpcException);
        }
    }

    private static AsyncPolicyWrap<EventDetailsReply> BuildPolicy()
    {
        var transientRpcPolicy = Policy<EventDetailsReply>
            .Handle<RpcException>(IsTransient)
            .AdvancedCircuitBreakerAsync(
                failureThreshold: 0.5,
                samplingDuration: TimeSpan.FromSeconds(20),
                minimumThroughput: 10,
                durationOfBreak: TimeSpan.FromSeconds(30));

        var retryPolicy = Policy<EventDetailsReply>
            .Handle<RpcException>(IsTransient)
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, retryAttempt - 1)));

        return retryPolicy.WrapAsync(transientRpcPolicy);
    }

    private static bool IsTransient(RpcException rpcException)
    {
        return rpcException.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded;
    }
}
