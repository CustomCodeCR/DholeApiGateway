using System.Threading.RateLimiting;
using CustomCodeFramework.Api.DependencyInjection;
using CustomCodeFramework.Auth.DependencyInjection;
using Dhole.ApiGateway.Gateway;
using Dhole.ApiGateway.Security;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

const string CorsPolicyName = "DholeWebCors";

builder.Services.AddCustomCodeApiWithSwagger(title: "Dhole Api Gateway", version: "v1");

builder.Services.AddCustomCodeAuth(builder.Configuration, addJwt: true, addApiKeys: false);

builder.Services.AddCors(options =>
{
    options.AddPolicy(
        CorsPolicyName,
        policy =>
        {
            policy
                .WithOrigins(
                    "http://localhost:5173",
                    "http://127.0.0.1:5173",
                    "http://192.168.1.193:5173",
                    "http://192.168.1.12:5173"
                )
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    );
});

builder
    .Services.AddOptions<GatewayOptions>()
    .Bind(builder.Configuration.GetSection(GatewayOptions.SectionName))
    .Validate(options => options.Routes.Count > 0, "Gateway routes are required.")
    .Validate(
        options => options.DefaultTimeoutSeconds > 0,
        "Gateway default timeout must be greater than zero."
    )
    .Validate(
        options => options.Routes.All(route => route.TimeoutSeconds is null or > 0),
        "Gateway route timeouts must be greater than zero."
    )
    .ValidateOnStart();

builder
    .Services.AddOptions<RateLimitingOptions>()
    .Bind(builder.Configuration.GetSection(RateLimitingOptions.SectionName))
    .Validate(
        options => options.PermitLimit > 0,
        "Rate limit permit limit must be greater than zero."
    )
    .Validate(options => options.WindowSeconds > 0, "Rate limit window must be greater than zero.")
    .ValidateOnStart();

builder.Services.AddHttpClient(
    "gateway",
    client =>
    {
        // Route-specific timeouts are controlled by GatewayProxyMiddleware.
        // Disabling HttpClient.Timeout prevents the fixed 100-second timeout
        // from cancelling long-running local AI requests.
        client.Timeout = Timeout.InfiniteTimeSpan;
    }
);

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var rateOptions = context
            .RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>()
            .Value;

        var key =
            context.User.Identity?.Name
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateOptions.PermitLimit,
                Window = TimeSpan.FromSeconds(rateOptions.WindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true,
            }
        );
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

app.UseCustomCodeApi();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(CorsPolicyName);

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<GatewayHeaderForwardingMiddleware>();
app.UseMiddleware<GatewayProxyMiddleware>();

app.MapGet(
        "/health",
        () =>
        {
            return Results.Ok(
                new
                {
                    service = "Dhole.ApiGateway",
                    status = "Healthy",
                    timestamp = DateTimeOffset.UtcNow,
                }
            );
        }
    )
    .AllowAnonymous();

app.Run();
