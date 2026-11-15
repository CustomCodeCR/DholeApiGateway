using System.Text.Json.Nodes;
using Dhole.ApiGateway.ThirdParty;
using Xunit;

namespace Dhole.ApiGateway.Realtime.Tests;

public sealed class ThirdPartyCatalogMetadataTests
{
    private static JsonObject Airport(string type, string country) => new()
    {
        ["code"] = "APT_MIA",
        ["name"] = "Miami",
        ["metadataJson"] = JsonSerializer(type, country)
    };

    private static string JsonSerializer(string type, string country) =>
        new JsonObject
        {
            ["terminalType"] = type,
            ["transportMode"] = type == "APT" ? "Air" : "Maritime",
            ["routeRole"] = "POL",
            ["countryCode"] = country,
            ["customAttribute"] = "preserved"
        }.ToJsonString();

    [Fact]
    public void Config_catalog_contract_offers_metadata_and_optional_terminal_filters()
    {
        var fields = ThirdPartyFieldCatalog.Outputs("config-catalog-pol");
        Assert.Contains(fields, x => x.Public == "metadata" && x.Internal == "metadata");
        Assert.Contains(fields, x => x.Public == "terminalType" && x.Internal == "metadataJson.terminalType");
        Assert.Contains(fields, x => x.Public == "countryCode" && x.Internal == "metadataJson.countryCode");
        Assert.Contains(ThirdPartyFieldCatalog.Inputs("config-catalog-pol"),
            x => x.Public == "terminalType" && !x.Required);
        Assert.DoesNotContain(ThirdPartyFieldCatalog.Inputs("config-catalog-incoterms"),
            x => x.Public == "terminalType");
    }

    [Fact]
    public void Nested_properties_are_real_metadata_and_complete_object_is_preserved()
    {
        var row = Airport("APT", "US");
        Assert.Equal("APT", ThirdPartyCatalogMetadata.Read(row, "metadataJson.terminalType")?.ToString());
        Assert.Equal("Air", ThirdPartyCatalogMetadata.Read(row, "metadataJson.transportMode")?.ToString());
        Assert.Equal("preserved", (ThirdPartyCatalogMetadata.Read(row, "metadata") as JsonObject)?["customAttribute"]?.ToString());
        Assert.Null(ThirdPartyCatalogMetadata.Read(row, "metadataJson.unknown"));
    }

    [Fact]
    public void Filter_uses_item_metadata_not_the_group_or_name()
    {
        var rows = new JsonArray(Airport("APT", "US"), Airport("CY", "CR"), Airport("SD", "CR"));
        var filtered = ThirdPartyCatalogMetadata.Filter(rows,
            new Dictionary<string, string> { ["terminalType"] = "cy", ["countryCode"] = "cr" });
        var only = Assert.Single(Assert.IsType<JsonArray>(filtered));
        Assert.Equal("CY", ThirdPartyCatalogMetadata.Read(only, "metadataJson.terminalType")?.ToString());
        Assert.Equal(3, rows.Count); // no mutation of upstream records
    }

    [Fact]
    public void Invalid_or_missing_metadata_is_not_misclassified()
    {
        var invalid = new JsonObject { ["metadataJson"] = "{invalid" };
        Assert.Null(ThirdPartyCatalogMetadata.Read(invalid, "metadata"));
        Assert.False(ThirdPartyCatalogMetadata.Matches(invalid,
            new Dictionary<string, string> { ["terminalType"] = "SD" }));
        var jsonObject = new JsonObject { ["metadataJson"] = new JsonObject { ["terminalType"] = "SD" } };
        Assert.Equal("SD", ThirdPartyCatalogMetadata.Read(jsonObject, "metadataJson.terminalType")?.ToString());
    }
}
