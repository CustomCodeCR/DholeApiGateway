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

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        var route = options
            .Value.Routes.OrderByDescending(x => x.Prefix.Length)
            .FirstOrDefault(x => path.StartsWith(x.Prefix, StringComparison.OrdinalIgnoreCase));

        if (route is null)
        {
            await next(context);
            return;
        }

        var targetUri = BuildTargetUri(context, route);

        logger.LogInformation(
            "Gateway forwarding {Method} {Path} to {TargetUri}",
            context.Request.Method,
            context.Request.Path,
            targetUri
        );

        using var requestMessage = CreateRequestMessage(context, targetUri);

        var client = httpClientFactory.CreateClient("gateway");

        using var responseMessage = await client.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseHeadersRead,
            context.RequestAborted
        );

        await CopyResponseAsync(context, responseMessage);
    }

    private static string BuildTargetUri(HttpContext context, GatewayRoute route)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var query = context.Request.QueryString.Value ?? string.Empty;

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
                continue;

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

        await responseMessage.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }
}
