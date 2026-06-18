using System.Security.Claims;

namespace Dhole.ApiGateway.Security;

public sealed class GatewayHeaderForwardingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var userId =
            context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub")
            ?? context.User.FindFirstValue("user_id");

        var userName =
            context.User.Identity?.Name
            ?? context.User.FindFirstValue("username")
            ?? context.User.FindFirstValue("name");

        var email = context.User.FindFirstValue("email");

        if (!string.IsNullOrWhiteSpace(userId))
            context.Request.Headers["X-User-Id"] = userId;

        if (!string.IsNullOrWhiteSpace(userName))
            context.Request.Headers["X-User-Name"] = userName;

        if (!string.IsNullOrWhiteSpace(email))
            context.Request.Headers["X-User-Email"] = email;

        context.Request.Headers["X-Forwarded-For"] = context.Connection.RemoteIpAddress?.ToString();

        context.Request.Headers["X-User-Agent"] = context.Request.Headers.UserAgent.ToString();

        if (!context.Request.Headers.ContainsKey("X-Correlation-Id"))
        {
            context.Request.Headers["X-Correlation-Id"] = context.TraceIdentifier;
        }

        await next(context);
    }
}
