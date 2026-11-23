using System.Threading.RateLimiting;
using CustomCodeFramework.Api.DependencyInjection;
using CustomCodeFramework.Auth.DependencyInjection;
using Dhole.ApiGateway.Gateway;
using Dhole.ApiGateway.Security;
using Dhole.ApiGateway.ThirdParty;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

var contentHttpUrl = builder.Configuration["CONTENT_HTTP_URL"]?.Trim().TrimEnd('/');
if (!string.IsNullOrWhiteSpace(contentHttpUrl))
{
    builder.Configuration.AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["Gateway:Routes:10:Prefix"] = "/api/content",
            ["Gateway:Routes:10:Destination"] = contentHttpUrl,
            ["Gateway:HealthChecks:content"] = contentHttpUrl,
        }
    );
}

var agentHttpUrl = builder.Configuration["AGENT_HTTP_URL"]?.Trim().TrimEnd('/');
if (!string.IsNullOrWhiteSpace(agentHttpUrl))
{
    builder.Configuration.AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["Gateway:Routes:11:Prefix"] = "/api/agents",
            ["Gateway:Routes:11:Destination"] = agentHttpUrl,
            ["Gateway:Routes:11:TimeoutSeconds"] = "600",
            ["Gateway:HealthChecks:agent"] = agentHttpUrl,
        }
    );
}

var hermesHttpUrl = builder.Configuration["HERMES_HTTP_URL"]?.Trim().TrimEnd('/');
if (!string.IsNullOrWhiteSpace(hermesHttpUrl))
{
    builder.Configuration.AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["Gateway:HealthChecks:hermes"] = hermesHttpUrl,
        }
    );
}

// CRM is an independently deployed service. Use the same destination for API requests and health checks.
var crmHttpUrl = builder.Configuration["CRM_HTTP_URL"]?.Trim().TrimEnd('/');
if (!string.IsNullOrWhiteSpace(crmHttpUrl))
{
    builder.Configuration.AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["Gateway:Routes:12:Prefix"] = "/api/crm",
            ["Gateway:Routes:12:Destination"] = crmHttpUrl,
            ["Gateway:Routes:12:StripPrefix"] = "false",
            ["Gateway:HealthChecks:crm"] = crmHttpUrl,
        }
    );
}


 // Operations routes and per-case SignalR Hub share one authenticated upstream.
 // Indexes 15/16 align with DholeSystem's overlay (13/14 are Dynamics routes).
var operationsHttpUrl = builder.Configuration["OPERATIONS_HTTP_URL"]?.Trim().TrimEnd('/');
if (!string.IsNullOrWhiteSpace(operationsHttpUrl))
{
    builder.Configuration.AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["Gateway:Routes:15:Prefix"] = "/api/operations",
            ["Gateway:Routes:15:Destination"] = operationsHttpUrl,
            ["Gateway:Routes:15:StripPrefix"] = "false",
            ["Gateway:Routes:15:TimeoutSeconds"] = "120",
            ["Gateway:Routes:16:Prefix"] = "/hubs/operations",
            ["Gateway:Routes:16:Destination"] = operationsHttpUrl,
            ["Gateway:Routes:16:StripPrefix"] = "false",
            ["Gateway:Routes:16:TimeoutSeconds"] = "120",
            ["Gateway:HealthChecks:operations"] = operationsHttpUrl,
        }
    );
}

const string CorsPolicyName = "DholeWebCors";

builder.Services.AddCustomCodeApiWithSwagger(title: "Dhole Api Gateway", version: "v1");

builder.Services.AddCustomCodeAuth(builder.Configuration, addJwt: true, addApiKeys: false);

// Register integration routes independently of storage readiness.
// Authorization and API Key restrictions remain enforced by the endpoints.
builder.Services.AddSingleton<ThirdPartyModuleReadiness>();
builder.Services.AddDbContext<ThirdPartyDbContext>(options =>
{
    var connection = builder.Configuration["ThirdParty:ConnectionString"];
    if (!string.IsNullOrWhiteSpace(connection))
        options.UseNpgsql(connection);
});


builder.Services.AddCors(options =>
{
    options.AddPolicy(
        CorsPolicyName,
        policy =>
        {
            var allowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (
                var configuredOrigin in builder.Configuration
                    .GetSection("Cors:AllowedOrigins")
                    .Get<string[]>() ?? Array.Empty<string>()
            )
            {
                if (!string.IsNullOrWhiteSpace(configuredOrigin))
                {
                    allowedOrigins.Add(configuredOrigin.Trim().TrimEnd('/'));
                }
            }

            static void AddEnvironmentOrigin(HashSet<string> origins, string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                var normalized = value.Trim().TrimEnd('/');

                if (
                    Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                )
                {
                    origins.Add(uri.GetLeftPart(UriPartial.Authority));
                    return;
                }

                normalized = normalized
                    .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
                    .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
                    .Trim('/');

                if (string.IsNullOrWhiteSpace(normalized))
                {
                    return;
                }

                origins.Add($"https://{normalized}");
                origins.Add($"http://{normalized}");
            }

            AddEnvironmentOrigin(allowedOrigins, builder.Configuration["DHOLE_WEB_HOST"]);
            AddEnvironmentOrigin(allowedOrigins, builder.Configuration["VITE_FRONTEND_DOMAIN"]);

            if (builder.Environment.IsDevelopment())
            {
                allowedOrigins.Add("http://localhost:5173");
                allowedOrigins.Add("http://127.0.0.1:5173");
            }

            policy
                .WithOrigins(allowedOrigins.ToArray())
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
                // Browsers may reuse successful preflights for cross-origin JWT requests.
                .SetPreflightMaxAge(TimeSpan.FromHours(1));
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

builder.Services.AddHttpClient("third-party-upstream", c => c.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });

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

        // Only validated identities bypass the global limiter. Checking a bearer
        // header alone would allow anonymous callers to disable throttling.
        // Login and refresh must stay protected even if a token is present.
        var path = context.Request.Path.Value?.TrimEnd('/') ?? string.Empty;
        var isCredentialEndpoint =
            path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/auth/refresh", StringComparison.OrdinalIgnoreCase);

        if (context.User.Identity?.IsAuthenticated == true && !isCredentialEndpoint)
        {
            return RateLimitPartition.GetNoLimiter("authenticated");
        }

        var key = context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

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

// Required before GatewayProxyMiddleware so Upgrade requests can be accepted and
// proxied to downstream SignalR/WebSocket endpoints.
app.UseWebSockets();

// Authenticate first so rate limiting can distinguish valid JWTs from
// unauthenticated traffic.
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// Keep the catalog and route discovery usable even if PostgreSQL is unavailable.
// Persistence and external traffic fail closed with a descriptive 503 rather than 404.
app.Use(async (context, next) =>
{
    var readiness = context.RequestServices.GetRequiredService<ThirdPartyModuleReadiness>();
    var path = context.Request.Path;
    var needsStorage = path.StartsWithSegments("/api/external") ||
        (path.StartsWithSegments("/api/integrations/third-party", out var suffix) &&
         (suffix.StartsWithSegments("/endpoints") || suffix.StartsWithSegments("/keys")));
    if (needsStorage && !readiness.StorageReady && !HttpMethods.IsOptions(context.Request.Method))
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new
        {
            code = "ThirdParty.StorageUnavailable",
            message = readiness.Message,
            traceId = context.TraceIdentifier,
        });
        return;
    }
    await next();
});

app.UseMiddleware<GatewayHeaderForwardingMiddleware>();
app.UseMiddleware<GatewayProxyMiddleware>();

static object CreateGatewayHealthResponse() =>
    new
    {
        service = "Dhole.ApiGateway",
        status = "Healthy",
        version = typeof(GatewayOptions).Assembly.GetName().Version?.ToString(3),
        timestamp = DateTimeOffset.UtcNow,
    };

app.MapGet("/health", () => Results.Ok(CreateGatewayHealthResponse())).AllowAnonymous();

// The registry contains declared versions. Health may supply the live version.
app.MapGet("/api/system/versions", (IWebHostEnvironment environment) =>
{
    var path = Path.Combine(environment.ContentRootPath, "service-versions.json");
    if (!File.Exists(path))
        return Results.Problem("Version manifest unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);

    return Results.Text(File.ReadAllText(path), "application/json");
}).AllowAnonymous();

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

            // CRM and Operations have dedicated dependency-readiness endpoints.
            // Operations /ready verifies both PostgreSQL and Redis, unlike /health.
            var healthPath = service.Equals("crm", StringComparison.OrdinalIgnoreCase)
                ? "/health/ready"
                : service.Equals("operations", StringComparison.OrdinalIgnoreCase)
                    ? "/ready"
                    : "/health";
            var healthUrl = $"{destination.Value.TrimEnd('/')}{healthPath}";
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

// Never hide the entire API Manager when its storage is unavailable.
var thirdPartyReadiness = app.Services.GetRequiredService<ThirdPartyModuleReadiness>();
if (string.IsNullOrWhiteSpace(app.Configuration["ThirdParty:ConnectionString"]))
{
    thirdPartyReadiness.Message = "El almacenamiento de APIs para terceros no está configurado en Gateway. Configurá ThirdParty:ConnectionString y reiniciá el servicio.";
    app.Logger.LogWarning("Third-party storage not configured; catalog available, persistence returns 503.");
}
else
{
    try
    {
        await ThirdPartyApiEndpoints.MigrateStorageAsync(app.Services);
        thirdPartyReadiness.StorageReady = true;
        thirdPartyReadiness.Message = "Almacenamiento y migraciones de APIs para terceros disponibles.";
        app.Logger.LogInformation("Third-party storage schema and EF migrations ready.");
    }
    catch (Exception exception)
    {
        var pgError = exception.GetBaseException() as Npgsql.PostgresException;
        var sqlState = pgError?.SqlState;
        thirdPartyReadiness.Message = "No se pudo iniciar el almacenamiento de APIs para terceros. " +
            (sqlState is null ? "Revisá PostgreSQL y las migraciones de Gateway." :
                "Código PostgreSQL SQLSTATE " + sqlState + ". Revisá el registro de Gateway.");
        app.Logger.LogError(exception, "Third-party migration failed; routes still registered; persistence returns 503.");
    }
}
app.MapThirdPartyEndpoints();

app.Run();

// Enables isolated TestServer Hub authentication/transport tests; no runtime effect.
public partial class Program { }
