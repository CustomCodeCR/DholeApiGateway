namespace Dhole.ApiGateway.Gateway;

public sealed class GatewayRoute
{
    public string Prefix { get; init; } = default!;
    public string Destination { get; init; } = default!;

    /// <summary>
    /// Removes the configured route prefix before forwarding the request.
    /// Example: /api/storage/health -> /health.
    /// </summary>
    public bool StripPrefix { get; init; }

    /// <summary>
    /// Maximum time the gateway waits for this route. When omitted,
    /// GatewayOptions.DefaultTimeoutSeconds is used.
    /// </summary>
    public int? TimeoutSeconds { get; init; }
}
