using System.Buffers;
using System.Net.WebSockets;
using Microsoft.Extensions.Options;

namespace Dhole.ApiGateway.Gateway;

public sealed class GatewayProxyMiddleware(
    RequestDelegate next,
    IHttpClientFactory httpClientFactory,
    IOptions<GatewayOptions> options,
    ILogger<GatewayProxyMiddleware> logger
)
{
    private static readonly HashSet<string> ExcludedHeaders =
    [
        "host",
        "connection",
        "transfer-encoding",
        "keep-alive",
        "proxy-authenticate",
        "proxy-authorization",
        "te",
        "trailer",
        "upgrade",
    ];

    private static readonly HashSet<string> WebSocketManagedHeaders =
    [
        "host",
        "connection",
        "upgrade",
        "content-length",
        "sec-websocket-key",
        "sec-websocket-version",
        "sec-websocket-extensions",
        "sec-websocket-protocol",
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        // CORS middleware runs before this proxy and applies the configured
        // Access-Control-* headers. A preflight must never be forwarded to a
        // downstream microservice; otherwise an unavailable destination turns
        // a valid CORS preflight into a 502 response.
        if (
            HttpMethods.IsOptions(context.Request.Method)
            && context.Request.Headers.ContainsKey("Origin")
            && context.Request.Headers.ContainsKey("Access-Control-Request-Method")
        )
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;

        var gatewayOptions = options.Value;
        var route = gatewayOptions
            .Routes.OrderByDescending(x => x.Prefix.Length)
            .FirstOrDefault(x => path.StartsWith(x.Prefix, StringComparison.OrdinalIgnoreCase));

        if (route is null)
        {
            await next(context);
            return;
        }

        var timeoutSeconds = route.TimeoutSeconds ?? gatewayOptions.DefaultTimeoutSeconds;

        if (context.WebSockets.IsWebSocketRequest)
        {
            await ProxyWebSocketAsync(context, route, timeoutSeconds);
            return;
        }

        var targetUri = BuildTargetUri(context, route);

        logger.LogInformation(
            "Gateway forwarding {Method} {Path} to {TargetUri} with timeout {TimeoutSeconds}s",
            context.Request.Method,
            context.Request.Path,
            targetUri,
            timeoutSeconds
        );

        using var requestMessage = CreateRequestMessage(context, targetUri);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            context.RequestAborted
        );
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var client = httpClientFactory.CreateClient("gateway");

        try
        {
            using var responseMessage = await client.SendAsync(
                requestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token
            );

            await CopyResponseAsync(context, responseMessage);
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(
                "Gateway timeout forwarding {Method} {Path} after {TimeoutSeconds}s",
                context.Request.Method,
                context.Request.Path,
                timeoutSeconds
            );

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
                context.Response.ContentType = "application/json";

                await context.Response.WriteAsJsonAsync(
                    new
                    {
                        code = "Gateway.Timeout",
                        message = $"El servicio no respondió dentro de {timeoutSeconds} segundos.",
                        traceId = context.TraceIdentifier,
                    },
                    CancellationToken.None
                );
            }
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(
                exception,
                "Gateway could not reach destination {TargetUri}",
                targetUri
            );

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                context.Response.ContentType = "application/json";

                await context.Response.WriteAsJsonAsync(
                    new
                    {
                        code = "Gateway.DestinationUnavailable",
                        message = "No fue posible comunicarse con el servicio de destino.",
                        traceId = context.TraceIdentifier,
                    },
                    CancellationToken.None
                );
            }
        }
    }

    private async Task ProxyWebSocketAsync(
        HttpContext context,
        GatewayRoute route,
        int timeoutSeconds
    )
    {
        var targetUri = BuildWebSocketTargetUri(context, route);

        logger.LogInformation(
            "Gateway forwarding WebSocket {Path} to {TargetUri}",
            context.Request.Path,
            targetUri
        );

        using var upstream = new ClientWebSocket();
        upstream.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        CopyWebSocketRequestHeaders(context, upstream.Options);
        CopyWebSocketSubProtocols(context, upstream.Options);

        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(
            context.RequestAborted
        );
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(Math.Min(timeoutSeconds, 30)));

        try
        {
            await upstream.ConnectAsync(targetUri, connectTimeout.Token);
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(
                "Gateway WebSocket connection to {TargetUri} timed out",
                targetUri
            );

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
                await context.Response.WriteAsJsonAsync(
                    new
                    {
                        code = "Gateway.WebSocketTimeout",
                        message = "El servicio WebSocket no respondió a tiempo.",
                        traceId = context.TraceIdentifier,
                    },
                    CancellationToken.None
                );
            }

            return;
        }
        catch (WebSocketException exception)
        {
            logger.LogError(
                exception,
                "Gateway could not establish WebSocket connection to {TargetUri}",
                targetUri
            );

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                await context.Response.WriteAsJsonAsync(
                    new
                    {
                        code = "Gateway.WebSocketDestinationUnavailable",
                        message = "No fue posible establecer la conexión WebSocket con el servicio de destino.",
                        traceId = context.TraceIdentifier,
                    },
                    CancellationToken.None
                );
            }

            return;
        }

        using var downstream = await context.WebSockets.AcceptWebSocketAsync(upstream.SubProtocol);
        using var proxyCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.RequestAborted
        );

        var clientToServer = PumpWebSocketAsync(
            downstream,
            upstream,
            proxyCancellation.Token
        );
        var serverToClient = PumpWebSocketAsync(
            upstream,
            downstream,
            proxyCancellation.Token
        );

        await Task.WhenAny(clientToServer, serverToClient);
        proxyCancellation.Cancel();

        try
        {
            await Task.WhenAll(clientToServer, serverToClient);
        }
        catch (OperationCanceledException)
        {
            // Expected when either side closes and the opposite receive loop is cancelled.
        }
        catch (WebSocketException exception)
        {
            logger.LogDebug(exception, "WebSocket proxy closed with a transport error");
        }
    }

    private static async Task PumpWebSocketAsync(
        WebSocket source,
        WebSocket destination,
        CancellationToken cancellationToken
    )
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);

        try
        {
            while (
                !cancellationToken.IsCancellationRequested
                && source.State is WebSocketState.Open or WebSocketState.CloseSent
                && destination.State is WebSocketState.Open or WebSocketState.CloseReceived
            )
            {
                var result = await source.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken
                );

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    if (destination.State == WebSocketState.Open)
                    {
                        await destination.CloseOutputAsync(
                            result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                            result.CloseStatusDescription,
                            CancellationToken.None
                        );
                    }

                    break;
                }

                await destination.SendAsync(
                    new ArraySegment<byte>(buffer, 0, result.Count),
                    result.MessageType,
                    result.EndOfMessage,
                    cancellationToken
                );
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void CopyWebSocketRequestHeaders(
        HttpContext context,
        ClientWebSocketOptions options
    )
    {
        foreach (var header in context.Request.Headers)
        {
            var key = header.Key.ToLowerInvariant();
            if (WebSocketManagedHeaders.Contains(key))
            {
                continue;
            }

            // ClientWebSocket manages the actual Upgrade handshake. Forward the
            // application/security context needed by downstream services.
            if (
                key is "authorization" or "cookie" or "origin" or "user-agent"
                || key.StartsWith("x-", StringComparison.Ordinal)
            )
            {
                options.SetRequestHeader(header.Key, header.Value.ToString());
            }
        }
    }

    private static void CopyWebSocketSubProtocols(
        HttpContext context,
        ClientWebSocketOptions options
    )
    {
        if (!context.Request.Headers.TryGetValue("Sec-WebSocket-Protocol", out var protocols))
        {
            return;
        }

        foreach (
            var protocol in protocols
                .SelectMany(value => value?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [])
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
        )
        {
            options.AddSubProtocol(protocol);
        }
    }

    private static Uri BuildWebSocketTargetUri(HttpContext context, GatewayRoute route)
    {
        var httpTarget = new Uri(BuildTargetUri(context, route), UriKind.Absolute);
        var builder = new UriBuilder(httpTarget)
        {
            Scheme = httpTarget.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                ? "wss"
                : "ws",
        };

        return builder.Uri;
    }

    private static string BuildTargetUri(HttpContext context, GatewayRoute route)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var query = context.Request.QueryString.Value ?? string.Empty;

        if (route.StripPrefix && path.StartsWith(route.Prefix, StringComparison.OrdinalIgnoreCase))
        {
            path = path[route.Prefix.Length..];

            if (string.IsNullOrWhiteSpace(path))
            {
                path = "/";
            }
            else if (!path.StartsWith('/'))
            {
                path = $"/{path}";
            }
        }

        return $"{route.Destination.TrimEnd('/')}{path}{query}";
    }

    private static HttpRequestMessage CreateRequestMessage(HttpContext context, string targetUri)
    {
        var requestMessage = new HttpRequestMessage(
            new HttpMethod(context.Request.Method),
            targetUri
        );

        CopyRequestHeaders(context, requestMessage);

        if (
            HttpMethods.IsPost(context.Request.Method)
            || HttpMethods.IsPut(context.Request.Method)
            || HttpMethods.IsPatch(context.Request.Method)
        )
        {
            requestMessage.Content = new StreamContent(context.Request.Body);

            if (!string.IsNullOrWhiteSpace(context.Request.ContentType))
            {
                requestMessage.Content.Headers.TryAddWithoutValidation(
                    "Content-Type",
                    context.Request.ContentType
                );
            }
        }

        return requestMessage;
    }

    private static void CopyRequestHeaders(HttpContext context, HttpRequestMessage requestMessage)
    {
        foreach (var header in context.Request.Headers)
        {
            if (ExcludedHeaders.Contains(header.Key.ToLowerInvariant()))
            {
                continue;
            }

            if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
            {
                requestMessage.Content ??= new StreamContent(context.Request.Body);

                requestMessage.Content.Headers.TryAddWithoutValidation(
                    header.Key,
                    header.Value.ToArray()
                );
            }
        }
    }

    private static async Task CopyResponseAsync(
        HttpContext context,
        HttpResponseMessage responseMessage
    )
    {
        context.Response.StatusCode = (int)responseMessage.StatusCode;

        foreach (var header in responseMessage.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in responseMessage.Content.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        context.Response.Headers.Remove("transfer-encoding");

        await responseMessage.Content.CopyToAsync(
            context.Response.Body,
            context.RequestAborted
        );
    }
}
