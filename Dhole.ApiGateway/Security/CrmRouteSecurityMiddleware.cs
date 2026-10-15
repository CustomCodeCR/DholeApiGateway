namespace Dhole.ApiGateway.Security;

/// <summary>Additional fail-closed boundary for /api/crm; downstream CRM still
/// validates the JWT, effective scopes and individual entity ownership.</summary>
public sealed class CrmRouteSecurityMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path=context.Request.Path.Value ?? "";
        if(!path.Equals("/api/crm",StringComparison.OrdinalIgnoreCase) &&
           !path.StartsWith("/api/crm/",StringComparison.OrdinalIgnoreCase))
        {
            await next(context);return;
        }
        // Never forward a client supplied internal service credential.
        context.Request.Headers.Remove("X-Dhole-Service-Key");
        context.Request.Headers.Remove("X-Internal-Service-Key");
        if(!configuration.GetValue("Features:CrmGatewayEnabled",false))
        {
            context.Response.StatusCode=StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new {code="Gateway.CrmDisabled"});return;
        }
        if(context.User.Identity?.IsAuthenticated!=true)
        {
            context.Response.StatusCode=StatusCodes.Status401Unauthorized;
            return;
        }
        await next(context);
    }
}
