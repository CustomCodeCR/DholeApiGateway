namespace Dhole.ApiGateway.Gateway;

public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    public IReadOnlyCollection<GatewayRoute> Routes { get; init; } = [];
}
