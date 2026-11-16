using CustomCodeFramework.Auth.Abstractions;
using System.Text.Json;

namespace Dhole.ApiGateway.ThirdParty;

/// <summary>
/// Issues restricted, short-lived JWTs for the eight fixed, read-only Config
/// catalogs and the single approved Pricing rate-request POST. All other routes
/// still require an explicitly provisioned service token. An external API Key
/// never becomes an upstream credential.
/// </summary>
public static class ThirdPartyServiceIdentity
{
    private const string CatalogPath = "/api/config/catalog-items/by-group/{catalogGroupSlug}";
    private const string CatalogViewScope = "config.catalog-items.view";
    private const string MachineId = "2b3fa87d-02b7-4a9a-bcdd-c61f6af70e61";
    private const string PricingRequestMachineId = "7c1d5bdc-80e2-4bc9-9ec4-f65ec114ed70";
    private const string PricingRequestCode = "pricing-rate-request-create";
    private const string PricingRequestPath = "/api/pricing/rate-requests/";
    private const string PricingRequestScope = "pricing.rate-request.create";

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
        return CanIssueCatalogIdentity(target, config) || CanIssuePricingRequestIdentity(target, config);
    }

    public static bool UsesAutomaticPricingRequestIdentity(ApprovedTarget target, IConfiguration config) =>
        string.IsNullOrWhiteSpace(config["ThirdParty:ServiceTokens:" + target.Code]) &&
        CanIssuePricingRequestIdentity(target, config);

    public static string? GetCredential(ApprovedTarget target, IConfiguration config, ITokenService issuer,
        PublishedEndpoint? definition = null)
    {
        var explicitToken = config["ThirdParty:ServiceTokens:" + target.Code];
        if (!string.IsNullOrWhiteSpace(explicitToken)) return explicitToken;
        if (CanIssuePricingRequestIdentity(target, config))
        {
            // The seller is bound to the server-side contract, never to an external field.
            if (definition is null || definition.Target != PricingRequestCode ||
                definition.Inputs.Any(field => field.Internal == "sellerUserId")) return null;
            var delegatedSellerId = PricingRequestMachineId;
            if (definition.Defaults?.TryGetValue("sellerUserId", out var configuredSeller) == true)
            {
                if (configuredSeller.ValueKind != JsonValueKind.String ||
                    !Guid.TryParse(configuredSeller.GetString(), out var userId) || userId == Guid.Empty)
                    return null;
                delegatedSellerId = userId.ToString();
            }
            return issuer.CreateToken(new TokenRequest
            {
                UserId = PricingRequestMachineId,
                UserName = "Castro Fallas",
                UserType = "service",
                Scopes = [PricingRequestScope],
                ExtraClaims = new Dictionary<string, string>
                {
                    ["integration_source"] = "dhole-api-manager",
                    ["integration_target"] = PricingRequestCode,
                    ["integration_seller_user_id"] = delegatedSellerId,
                }
            });
        }
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

    private static bool HasJwtSigningConfiguration(IConfiguration config) =>
        !string.IsNullOrWhiteSpace(config["Auth:Jwt:Issuer"]) &&
        !string.IsNullOrWhiteSpace(config["Auth:Jwt:Audience"]) &&
        (config["Auth:Jwt:SecretKey"]?.Length ?? 0) >= 32;

    private static bool CanIssuePricingRequestIdentity(ApprovedTarget target, IConfiguration config) =>
        target.Code.Equals(PricingRequestCode, StringComparison.Ordinal) &&
        target.Service.Equals("pricing", StringComparison.Ordinal) &&
        target.Method.Equals("POST", StringComparison.Ordinal) &&
        target.Path.Equals(PricingRequestPath, StringComparison.Ordinal) &&
        HasJwtSigningConfiguration(config);

    private static bool CanIssueCatalogIdentity(ApprovedTarget target, IConfiguration config) =>
        target.Service.Equals("config", StringComparison.Ordinal) &&
        target.Method.Equals("GET", StringComparison.Ordinal) &&
        target.Path.Equals(CatalogPath, StringComparison.Ordinal) &&
        target.CatalogSlug is not null &&
        AllowedCatalogs.TryGetValue(target.Code, out var allowedSlug) &&
        target.CatalogSlug.Equals(allowedSlug, StringComparison.Ordinal) &&
        HasJwtSigningConfiguration(config);
}
