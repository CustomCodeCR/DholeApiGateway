namespace Dhole.ApiGateway.Gateway;

public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    public int DefaultTimeoutSeconds { get; init; } = 100;

    public int HealthTimeoutSeconds { get; init; } = 10;

    public IReadOnlyCollection<GatewayRoute> Routes { get; init; } = [];

    /// <summary>
    /// Internal service base addresses used by the public health aliases exposed by the gateway.
    /// The frontend must never call these destinations directly.
    /// </summary>
    public Dictionary<string, string> HealthChecks { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}
