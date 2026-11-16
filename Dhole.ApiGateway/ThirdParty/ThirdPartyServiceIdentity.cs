using CustomCodeFramework.Auth.Abstractions;

namespace Dhole.ApiGateway.ThirdParty;

/// <summary>
/// Issues a restricted machine identity only for the eight fixed, read-only Config
/// catalogs. All other operations continue to require an explicitly provisioned
/// per-route service token. An API Key never becomes an upstream credential.
/// </summary>
public static class ThirdPartyServiceIdentity
{
    private const string CatalogPath = "/api/config/catalog-items/by-group/{catalogGroupSlug}";
    private const string CatalogViewScope = "config.catalog-items.view";
    private const string MachineId = "2b3fa87d-02b7-4a9a-bcdd-c61f6af70e61";

    private static readonly IReadOnlyDictionary<string, string> AllowedCatalogs =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["config-catalog-pol"] = "pol",
            ["config-catalog-poe"] = "poe",
            ["config-catalog-pod"] = "pod",
            ["config-catalog-incoterms"] = "incoterms",
            ["config-catalog-container-sizes"] = "container-sizes",
            ["config-catalog-container-types"] = "container-types",
            ["config-catalog-trailer-sizes"] = "land-equipment-sizes",
            ["config-catalog-trailer-types"] = "land-equipment-types",
        };

    public static bool IsReady(ApprovedTarget? target, IConfiguration config)
    {
        if (target is null) return false;
        if (!string.IsNullOrWhiteSpace(config["ThirdParty:ServiceTokens:" + target.Code]))
            return true;
        return CanIssueCatalogIdentity(target, config);
    }

    public static string? GetCredential(ApprovedTarget target, IConfiguration config, ITokenService issuer)
    {
        var explicitToken = config["ThirdParty:ServiceTokens:" + target.Code];
        if (!string.IsNullOrWhiteSpace(explicitToken)) return explicitToken;
        if (!CanIssueCatalogIdentity(target, config)) return null;

        // Scope is hardcoded and deliberately cannot be controlled by a caller,
        // endpoint mapping, API Key, or dynamically discovered route.
        return issuer.CreateToken(new TokenRequest
        {
            UserId = MachineId,
            UserName = "dhole-api-manager-config",
            UserType = "service",
            Scopes = [CatalogViewScope],
            ExtraClaims = new Dictionary<string, string>
            {
                ["integration_source"] = "dhole-api-manager",
                ["integration_target"] = target.Code,
            }
        });
    }

    private static bool CanIssueCatalogIdentity(ApprovedTarget target, IConfiguration config) =>
        target.Service.Equals("config", StringComparison.Ordinal) &&
        target.Method.Equals("GET", StringComparison.Ordinal) &&
        target.Path.Equals(CatalogPath, StringComparison.Ordinal) &&
        target.CatalogSlug is not null &&
        AllowedCatalogs.TryGetValue(target.Code, out var allowedSlug) &&
        target.CatalogSlug.Equals(allowedSlug, StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(config["Auth:Jwt:Issuer"]) &&
        !string.IsNullOrWhiteSpace(config["Auth:Jwt:Audience"]) &&
        (config["Auth:Jwt:SecretKey"]?.Length ?? 0) >= 32;
}
