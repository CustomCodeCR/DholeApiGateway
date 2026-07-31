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
            var allowedOrigins = builder.Configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>();

            if (allowedOrigins is { Length: > 0 })
            {
                policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
                return;
            }

            if (builder.Environment.IsDevelopment())
            {
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
                return;
            }

            policy.WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod();
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
    .Validate(
        options => options.HealthTimeoutSeconds > 0,
        "Gateway health timeout must be greater than zero."
    )
    .Validate(
        options => options.HealthChecks.Count > 0,
        "Gateway health check destinations are required."
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

static object CreateGatewayHealthResponse() =>
    new
    {
        service = "Dhole.ApiGateway",
        status = "Healthy",
        timestamp = DateTimeOffset.UtcNow,
    };

app.MapGet("/health", () => Results.Ok(CreateGatewayHealthResponse())).AllowAnonymous();

app.MapGet(
        "/api/health/{service}",
        async (
            string service,
            HttpContext context,
            IHttpClientFactory httpClientFactory,
            IOptions<GatewayOptions> gatewayOptions,
            ILoggerFactory loggerFactory
        ) =>
        {
            if (service.Equals("gateway", StringComparison.OrdinalIgnoreCase))
            {
                await context.Response.WriteAsJsonAsync(CreateGatewayHealthResponse());
                return;
            }

            var options = gatewayOptions.Value;
            var destination = options.HealthChecks.FirstOrDefault(item =>
                item.Key.Equals(service, StringComparison.OrdinalIgnoreCase)
            );

            if (string.IsNullOrWhiteSpace(destination.Key) || string.IsNullOrWhiteSpace(destination.Value))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsJsonAsync(
                    new
                    {
                        code = "Gateway.HealthServiceNotFound",
                        message = $"No existe un health check configurado para '{service}'.",
                        traceId = context.TraceIdentifier,
                    }
                );
                return;
            }

            var healthUrl = $"{destination.Value.TrimEnd('/')}/health";
            var logger = loggerFactory.CreateLogger("GatewayHealthCheck");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.HealthTimeoutSeconds));

            try
            {
                var client = httpClientFactory.CreateClient("gateway");
                using var request = new HttpRequestMessage(HttpMethod.Get, healthUrl);
                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token
                );

                context.Response.StatusCode = (int)response.StatusCode;
                context.Response.ContentType =
                    response.Content.Headers.ContentType?.ToString() ?? "application/json";
                context.Response.Headers["Cache-Control"] = "no-store, no-cache";
                context.Response.Headers["X-Dhole-Health-Service"] = service;

                await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
            }
            catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
            {
                logger.LogWarning(
                    "Health check for {Service} timed out after {TimeoutSeconds}s",
                    service,
                    options.HealthTimeoutSeconds
                );

                context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
                await context.Response.WriteAsJsonAsync(
                    new
                    {
                        code = "Gateway.HealthTimeout",
                        message = $"El health check de '{service}' superó el tiempo máximo configurado.",
                        traceId = context.TraceIdentifier,
                    },
                    CancellationToken.None
                );
            }
            catch (HttpRequestException exception)
            {
                logger.LogWarning(exception, "Health check for {Service} could not reach its destination", service);

                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                await context.Response.WriteAsJsonAsync(
                    new
                    {
                        code = "Gateway.HealthDestinationUnavailable",
                        message = $"No fue posible consultar el health check de '{service}'.",
                        traceId = context.TraceIdentifier,
                    },
                    CancellationToken.None
                );
            }
        }
    )
    .AllowAnonymous();

app.Run();
