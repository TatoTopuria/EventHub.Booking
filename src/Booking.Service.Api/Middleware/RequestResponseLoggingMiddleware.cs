namespace Booking.Service.Api.Middleware;

public sealed class RequestResponseLoggingMiddleware(RequestDelegate next, ILogger<RequestResponseLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var hostName = Environment.MachineName;
        logger.LogInformation("HTTP {Host} {Method} {Path}", hostName, context.Request.Method, context.Request.Path);
        await next(context);
        logger.LogInformation("HTTP {Host} {Method} {Path} -> {StatusCode}", hostName, context.Request.Method, context.Request.Path, context.Response.StatusCode);
    }
}
