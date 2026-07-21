namespace Dhole.ApiGateway.Gateway;

public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    public int DefaultTimeoutSeconds { get; init; } = 100;

    public IReadOnlyCollection<GatewayRoute> Routes { get; init; } = [];
}
