using FluentValidation;
using Booking.Service.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Booking.Service.Api.Middleware;

public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException validationException)
        {
            logger.LogWarning(validationException, "Validation failure for request {Path}", context.Request.Path);
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, "Validation failed", validationException.Message);
        }
        catch (DbUpdateConcurrencyException concurrencyException)
        {
            // Thrown by EF Core when a tracked entity's concurrency token did not match on UPDATE/DELETE.
            // F6 uses this on EventSeatEntity.Version to surface "seat reserved by another caller while
            // you were holding the seat-reservation lock" as a clean 409 instead of a 500.
            logger.LogWarning(
                concurrencyException,
                "Concurrency conflict on request {Path}",
                context.Request.Path);
            await WriteProblemAsync(
                context,
                StatusCodes.Status409Conflict,
                "Concurrency conflict",
                "The resource was modified by another request. Please retry.");
        }
        catch (CatalogUnavailableException catalogException)
        {
            logger.LogWarning(catalogException, "Catalog service unavailable for request {Path}", context.Request.Path);
            await WriteProblemAsync(context, StatusCodes.Status503ServiceUnavailable, "Catalog unavailable", catalogException.Message);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled exception for request {Path}", context.Request.Path);
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, "Unexpected error", "An unexpected error occurred.");
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, int statusCode, string title, string detail)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var correlationId = context.Items.TryGetValue(CorrelationIdMiddleware.CorrelationItemName, out var value)
            ? value?.ToString()
            : null;

        var payload = new
        {
            title,
            status = statusCode,
            detail,
            correlationId
        };

        await context.Response.WriteAsJsonAsync(payload);
    }
}
