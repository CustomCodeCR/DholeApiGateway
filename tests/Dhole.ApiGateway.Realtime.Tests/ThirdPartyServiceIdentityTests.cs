using CustomCodeFramework.Auth.Abstractions;
using Dhole.ApiGateway.ThirdParty;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Dhole.ApiGateway.Realtime.Tests;

public sealed class ThirdPartyServiceIdentityTests
{
    private static IConfiguration ValidConfig() => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["Auth:Jwt:Issuer"] = "DholeAuthService",
            ["Auth:Jwt:Audience"] = "DholeEcosystem",
            ["Auth:Jwt:SecretKey"] = new string('X', 48),
        }).Build();

    private static ApprovedTarget ConfigTarget(string code = "config-catalog-pol", string slug = "pol") =>
        new(code, "config", "GET", "/api/config/catalog-items/by-group/{catalogGroupSlug}", "Config")
        { CatalogSlug = slug };

    [Fact]
    public void Fixed_readonly_catalog_uses_only_a_service_view_scope()
    {
        var issuer = new InspectingIssuer();
        var target = ConfigTarget();
        Assert.True(ThirdPartyServiceIdentity.IsReady(target, ValidConfig()));
        Assert.Equal("restricted-token", ThirdPartyServiceIdentity.GetCredential(target, ValidConfig(), issuer));
        var request = Assert.IsType<TokenRequest>(issuer.LastRequest);
        Assert.Equal("service", request.UserType);
        Assert.Equal("config.catalog-items.view", Assert.Single(request.Scopes));
        Assert.Empty(request.Permissions);
        Assert.Empty(request.Roles);
        Assert.Null(request.SessionId);
    }

    [Theory]
    [InlineData("config-catalog-unknown", "pol")]
    [InlineData("config-catalog-pol", "poe")]
    public void Unknown_catalog_and_slug_mismatch_are_not_automatically_authorized(string code, string slug)
    {
        var target = ConfigTarget(code, slug);
        Assert.False(ThirdPartyServiceIdentity.IsReady(target, ValidConfig()));
        Assert.Null(ThirdPartyServiceIdentity.GetCredential(target, ValidConfig(), new InspectingIssuer()));
    }

    [Fact]
    public void Mutations_and_discovered_routes_cannot_gain_automatic_identity()
    {
        var create = new ApprovedTarget("route.config.post.xyz", "config", "POST",
            "/api/config/catalog-items/by-group/{catalogGroupSlug}", "Mutation") { CatalogSlug = "pol" };
        var discovered = new ApprovedTarget("route.config.get.xyz", "config", "GET",
            "/api/config/catalog-items/by-group/{catalogGroupSlug}", "Discovery") { CatalogSlug = "pol" };
        Assert.False(ThirdPartyServiceIdentity.IsReady(create, ValidConfig()));
        Assert.False(ThirdPartyServiceIdentity.IsReady(discovered, ValidConfig()));
    }

    [Fact]
    public void Missing_signing_key_fails_closed_and_explicit_token_is_preserved()
    {
        var target = ConfigTarget();
        var empty = new ConfigurationBuilder().Build();
        Assert.False(ThirdPartyServiceIdentity.IsReady(target, empty));
        var withExplicit = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ThirdParty:ServiceTokens:config-catalog-pol"] = "configured-credential" }).Build();
        Assert.True(ThirdPartyServiceIdentity.IsReady(target, withExplicit));
        Assert.Equal("configured-credential", ThirdPartyServiceIdentity.GetCredential(target, withExplicit, new InspectingIssuer()));
    }

    private sealed class InspectingIssuer : ITokenService
    {
        public TokenRequest? LastRequest { get; private set; }
        public string CreateToken(TokenRequest request)
        {
            LastRequest = request;
            return "restricted-token";
        }
    }
}
