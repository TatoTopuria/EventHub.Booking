using Microsoft.AspNetCore.Mvc.Filters;

namespace Booking.Service.Api.Filters;

public sealed class AuditActionFilter(ILogger<AuditActionFilter> logger) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var caller = context.HttpContext.User.Identity?.Name ?? "anonymous";
        var action = context.ActionDescriptor.DisplayName ?? "unknown-action";

        logger.LogInformation("Audit caller={Caller} action={Action}", caller, action);
        await next();
    }
}
