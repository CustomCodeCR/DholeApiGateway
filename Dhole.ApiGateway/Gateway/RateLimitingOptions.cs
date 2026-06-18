namespace Dhole.ApiGateway.Gateway;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public int PermitLimit { get; init; } = 120;

    public int WindowSeconds { get; init; } = 60;
}
