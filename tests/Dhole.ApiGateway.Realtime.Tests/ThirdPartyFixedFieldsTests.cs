using System.Text.Json.Nodes;
using Dhole.ApiGateway.ThirdParty;
using Xunit;

namespace Dhole.ApiGateway.Realtime.Tests;

public sealed class ThirdPartyFixedFieldsTests
{
    private static ApprovedTarget Target(string method = "POST") =>
        new("pricing-rate-request-create", "pricing", method, "/api/pricing/rate-requests/", "Solicitud");

    private static PublishedEndpoint Contract(JsonNode? value, string field = "sellerUserId") =>
        new(Guid.NewGuid(), "Solicitud", "solicitar-tarifa", "pricing-rate-request-create",
            [new PublishedField(field, field, true, value)], [], null, false);

    [Fact]
    public void Accepts_fixed_seller_identity_on_post()
    {
        Assert.Null(ThirdPartyFixedFields.Validate(
            Contract(JsonValue.Create("a33243e0-d976-4c49-96ee-21edaf8cd249")), Target()));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("DELETE")]
    public void Rejects_defaults_on_unapproved_methods(string method)
    {
        Assert.NotNull(ThirdPartyFixedFields.Validate(Contract(JsonValue.Create("hello"), "clientName"), Target(method)));
    }

    [Fact]
    public void Rejects_empty_seller_uuid_and_nested_query_binding()
    {
        Assert.NotNull(ThirdPartyFixedFields.Validate(Contract(JsonValue.Create("unknown")), Target()));
        Assert.NotNull(ThirdPartyFixedFields.Validate(
            new PublishedEndpoint(Guid.NewGuid(), "Example", "example-api", "route.crm.post.xxx",
                [new PublishedField("client", "query.client", false, JsonValue.Create("fixed"))], [], null, false),
            Target()));
    }

    [Fact]
    public void Detects_explicit_nested_override_even_if_caller_sent_null()
    {
        var sent = JsonNode.Parse("""{"body":{"clientId":null}}""")!.AsObject();
        Assert.True(ThirdPartyFixedFields.ContainsPath(sent, "body.clientId"));
        Assert.False(ThirdPartyFixedFields.ContainsPath(sent, "body.userId"));
    }
}
