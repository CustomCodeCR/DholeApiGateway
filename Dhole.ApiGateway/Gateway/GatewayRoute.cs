namespace Dhole.ApiGateway.Gateway;

public sealed class GatewayRoute
{
    public string Prefix { get; init; } = default!;
    public string Destination { get; init; } = default!;

    /// <summary>
    /// Maximum time the gateway waits for this route. When omitted,
    /// GatewayOptions.DefaultTimeoutSeconds is used.
    /// </summary>
    public int? TimeoutSeconds { get; init; }
}
