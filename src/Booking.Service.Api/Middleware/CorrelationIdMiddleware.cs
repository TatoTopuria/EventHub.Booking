namespace Booking.Service.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string CorrelationHeaderName = "X-Correlation-ID";
    public const string CorrelationItemName = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[CorrelationHeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString("N");
        }

        context.Items[CorrelationItemName] = correlationId;
        context.Response.Headers[CorrelationHeaderName] = correlationId;

        await next(context);
    }
}
