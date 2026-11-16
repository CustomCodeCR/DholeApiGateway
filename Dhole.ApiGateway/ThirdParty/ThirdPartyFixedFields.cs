using System.Text.Json.Nodes;

namespace Dhole.ApiGateway.ThirdParty;

/// <summary>Validate contract-owned values: they never grant additional scopes and cannot be overridden by clients.</summary>
public static class ThirdPartyFixedFields
{
    public static string? Validate(PublishedEndpoint definition, ApprovedTarget target)
    {
        foreach (var field in definition.Inputs)
        {
            if (field.DefaultValue is null) continue;
            if (target.Method is not ("POST" or "PUT" or "PATCH"))
                return "Los valores predeterminados solo se permiten en POST, PUT y PATCH.";
            if (field.Internal.StartsWith("path.", StringComparison.Ordinal) ||
                field.Internal.StartsWith("query.", StringComparison.Ordinal))
                return "No se permiten valores fijos para parámetros de ruta o consulta.";
            var serialized = field.DefaultValue.ToJsonString();
            if (serialized.Length is < 1 or > 4096)
                return "El valor predeterminado supera el límite permitido.";
            if (field.DefaultValue is JsonArray)
                return "Seleccioná valores fijos de tipo texto, número, booleano u objeto JSON.";
            if (field.Internal is "sellerUserId" or "executiveUserId")
            {
                if (field.DefaultValue is not JsonValue || !Guid.TryParse(field.DefaultValue.ToString(), out var userId) ||
                    userId == Guid.Empty)
                    return "El ejecutivo predeterminado requiere un UUID válido.";
            }
            if (field.Internal is "clientName" or "executiveName")
            {
                if (field.DefaultValue is not JsonValue ||
                    field.DefaultValue.ToString().Trim().Length is < 1 or > 180)
                    return "Cliente y ejecutivo requieren un nombre predeterminado válido.";
            }
        }
        return null;
    }

    public static bool ContainsPath(JsonObject obj, string path)
    {
        JsonNode? current = obj;
        foreach (var segment in path.Split('.'))
        {
            if (current is not JsonObject node || !node.TryGetPropertyValue(segment, out current))
                return false;
        }
        return true;
    }
}
