using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dhole.ApiGateway.ThirdParty;

/// <summary>Projects Config's item-level metadata without guessing its terminal type from the catalog.</summary>
public static class ThirdPartyCatalogMetadata
{
    public static JsonNode? Read(JsonNode? item, string path)
    {
        if (item is not JsonObject row || !row.TryGetPropertyValue("metadataJson", out var stored) || stored is null)
            return null;
        JsonNode? metadata = stored;
        if (stored is JsonValue value)
        {
            if (!value.TryGetValue<string>(out var json) || string.IsNullOrWhiteSpace(json) || json.Length > 65_536)
                return null;
            try { metadata = JsonNode.Parse(json); }
            catch (JsonException) { return null; }
        }

        if (path == "metadata") return metadata;
        if (!path.StartsWith("metadataJson.", StringComparison.Ordinal)) return null;
        foreach (var key in path["metadataJson.".Length..].Split('.'))
        {
            if (metadata is not JsonObject obj) return null;
            metadata = obj[key];
        }
        return metadata;
    }

    public static bool Matches(JsonNode? item, IReadOnlyDictionary<string, string> filters) =>
        filters.All(filter => string.Equals(
            Read(item, "metadataJson." + filter.Key)?.ToString(),
            filter.Value, StringComparison.OrdinalIgnoreCase));

    public static JsonNode? Filter(JsonNode? data, IReadOnlyDictionary<string, string> filters)
    {
        if (filters.Count == 0) return data;
        if (data is JsonArray rows)
            return new JsonArray(rows.Where(item => item is not null && Matches(item, filters))
                .Select(item => item!.DeepClone()).ToArray());
        return data is JsonObject && Matches(data, filters) ? data : null;
    }
}
