namespace Dhole.ApiGateway.Gateway;

public sealed class GatewayRoute
{
    public string Prefix { get; init; } = default!;
    public string Destination { get; init; } = default!;
}
