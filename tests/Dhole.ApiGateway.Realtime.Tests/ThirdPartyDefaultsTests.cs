using System.Reflection;
using System.Text.Json;
using Dhole.ApiGateway.Gateway;
using Dhole.ApiGateway.ThirdParty;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Dhole.ApiGateway.Realtime.Tests;

public class ThirdPartyDefaultsTests
{
    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ThirdParty:Targets:0:Code"] = "pricing-rate-request-create",
            ["ThirdParty:Targets:0:Service"] = "pricing",
            ["ThirdParty:Targets:0:Method"] = "POST",
            ["ThirdParty:Targets:0:Path"] = "/api/pricing/rate-requests/",
            ["ThirdParty:Targets:0:Description"] = "Request a quote",
        }).Build();

    private static PublishedEndpoint Endpoint(Dictionary<string, JsonElement>? defaults = null,
        List<PublishedField>? fields = null) => new(Guid.NewGuid(),
        "Website Rate Request", "website-rate-request", "pricing-rate-request-create",
        fields ?? [
            new("priority", "priority", true),
            new("payload", "payload", false)
        ],
        [], null, false, defaults);

    private static string? Validate(PublishedEndpoint value)
    {
        var validate = typeof(ThirdPartyApiEndpoints).GetMethod(
            "Validate", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string?)validate.Invoke(null, [value, Configuration(), new GatewayOptions()]);
    }

    private static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Pricing_request_can_fix_seller_and_client_on_server()
    {
        var endpoint = Endpoint(new Dictionary<string, JsonElement>
        {
            ["sellerUserId"] = Element("\"b046f6c9-25d3-49aa-9ef0-71e54b8a74f2\""),
            ["clientName"] = Element("\"CLIENTE WEB\""),
            ["executiveName"] = Element("\"Vendedor Web\"")
        });
        Assert.Null(Validate(endpoint));
        var rehydrated = JsonSerializer.Deserialize<PublishedEndpoint>(
            JsonSerializer.Serialize(endpoint, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(3, rehydrated?.Defaults?.Count);
    }

    [Fact]
    public void Fixed_fields_cannot_also_be_exposed_to_clients()
    {
        var endpoint = Endpoint(new Dictionary<string, JsonElement>
        {
            ["sellerUserId"] = Element("\"b046f6c9-25d3-49aa-9ef0-71e54b8a74f2\"")
        }, [
            new("priority", "priority", true),
            new("sellerUserId", "sellerUserId", true),
            new("payload", "payload", false)
        ]);
        Assert.NotNull(Validate(endpoint));
    }

    [Fact]
    public void Unknown_defaults_and_nested_conflicts_are_rejected()
    {
        Assert.NotNull(Validate(Endpoint(new Dictionary<string, JsonElement>
        {
            ["unapprovedProperty"] = Element("123")
        })));
        Assert.NotNull(Validate(Endpoint(new Dictionary<string, JsonElement>
        {
            ["payload.form"] = Element("{}")
        }, [
            new("priority", "priority", true),
            new("payload", "payload", false)
        ])));
    }

    [Fact]
    public void Existing_contract_without_defaults_remains_compatible()
    {
        Assert.Null(Validate(Endpoint()));
        Assert.Null(Endpoint().Defaults);
    }
}
