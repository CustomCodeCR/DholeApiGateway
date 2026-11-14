using Dhole.ApiGateway.Gateway;
using Dhole.ApiGateway.ThirdParty;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Dhole.ApiGateway.Realtime.Tests;

public sealed class ThirdPartyRouteDiscoveryTests
{
    private static GatewayOptions Registry() => new()
    {
        Routes =
        [
            new GatewayRoute { Prefix = "/api/crm", Destination = "http://crm-api:8080" },
            new GatewayRoute { Prefix = "/api/config", Destination = "http://config-api:8080" },
            new GatewayRoute { Prefix = "/api/operations", Destination = "http://operations-api:8080" },
            new GatewayRoute { Prefix = "/api/dynamics-connector", Destination = "http://dynamics-api:8080/api/v1", StripPrefix = true }
        ]
    };

    private static IConfiguration Configuration() => new ConfigurationBuilder().Build();

    [Fact]
    public void Services_come_from_the_entire_gateway_registry()
    {
        var services = ThirdPartyRouteDiscovery.Services(Registry());
        Assert.Equal(4, services.Count);
        Assert.Contains(services, x => x.Service == "crm");
        Assert.Contains(services, x => x.Service == "config");
        Assert.Contains(services, x => x.Service == "operations");
        Assert.Contains(services, x => x.Service == "dynamics-connector");
    }

    [Theory]
    [InlineData("crm", "GET", "/api/crm/accounts/{id:guid}")]
    [InlineData("config", "PATCH", "/api/config/catalog-items/{catalogItemId:guid}")]
    [InlineData("operations", "GET", "/api/operations/realtime/events")]
    [InlineData("dynamics-connector", "POST", "/api/dynamics-connector/jobs")]
    public void Valid_routes_are_canonical_and_keep_their_method(string service, string method, string path)
    {
        var target = ThirdPartyRouteDiscovery.Resolve(
            new RouteSelectionRequest(service, method, path), Configuration(), Registry());
        Assert.NotNull(target);
        Assert.Equal(service, target.Service);
        Assert.Equal(method, target.Method);
        Assert.Equal(path, target.Path);
        Assert.Equal(target.Code, ThirdPartyRouteDiscovery.Resolve(target.Code, Configuration(), Registry())?.Code);
        Assert.False(target.Ready); // no technical token means no publication
    }

    [Fact]
    public void Route_params_are_required_and_encoded_as_individual_values()
    {
        const string template = "/api/crm/accounts/{id:guid}/contacts/{contactId:guid}";
        Assert.Equal(["id", "contactId"], ThirdPartyRouteDiscovery.PathNames(template));
        Assert.Equal("/api/crm/accounts/hello/contacts/abc%20123",
            ThirdPartyRouteDiscovery.BuildPath(template,
                new Dictionary<string, string> { ["id"] = "hello", ["contactId"] = "abc 123" }));
        Assert.Null(ThirdPartyRouteDiscovery.BuildPath(template,
            new Dictionary<string, string> { ["id"] = "hello" }));
        Assert.Null(ThirdPartyRouteDiscovery.BuildPath(template,
            new Dictionary<string, string> { ["id"] = "../", ["contactId"] = "x" }));
    }

    [Theory]
    [InlineData("crm", "GET", "/api/config/catalog-items")]
    [InlineData("crm", "GET", "/api/crm/../auth/token")]
    [InlineData("crm", "TRACE", "/api/crm/accounts")]
    [InlineData("missing", "GET", "/api/missing/accounts")]
    [InlineData("crm", "GET", "http://evil.example/api/crm/accounts")]
    public void Unsupported_routes_cannot_be_saved_or_forwarded(string service, string method, string path)
    {
        Assert.Null(ThirdPartyRouteDiscovery.Resolve(
            new RouteSelectionRequest(service, method, path), Configuration(), Registry()));
    }
}
