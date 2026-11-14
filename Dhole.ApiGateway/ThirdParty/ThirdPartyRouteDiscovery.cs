using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dhole.ApiGateway.Gateway;

namespace Dhole.ApiGateway.ThirdParty;

// Route discovery is metadata-only. Nothing discovered is publicly accessible until
// an administrator explicitly creates a published contract and a route-specific token exists.
public sealed record DiscoveredService(string Service, string Prefix);
public sealed record RouteSelectionRequest(string Service, string Method, string Path);
public sealed record RouteDiscoveryResult(string Service, bool Documented, string Message, IReadOnlyList<ApprovedTarget> Routes);

public static class ThirdPartyRouteDiscovery
{
    private static readonly string[] Verbs = ["GET", "POST", "PUT", "PATCH", "DELETE"];
    private static readonly Regex Parameter = new(@"\{([a-zA-Z][a-zA-Z0-9_]*)(?::[^}]+)?\}", RegexOptions.Compiled);
    private static readonly Regex SafePath = new(@"^/api/[a-z0-9-]+(?:/[a-zA-Z0-9_.-]+|/\{[a-zA-Z][a-zA-Z0-9_]*(?::[a-zA-Z0-9()|_-]+)?\})*/?$", RegexOptions.Compiled);
    private static readonly Regex SafeService = new(@"^[a-z0-9-]{2,40}$", RegexOptions.Compiled);

    public static IReadOnlyList<DiscoveredService> Services(GatewayOptions gateway) =>
        gateway.Routes.Select(x => x.Prefix.TrimEnd('/'))
            .Where(prefix => Regex.IsMatch(prefix, @"^/api/[a-z0-9-]+$"))
            .Select(prefix => new DiscoveredService(prefix[5..], prefix))
            .GroupBy(x => x.Service, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First()).OrderBy(x => x.Service).ToArray();

    public static string Encode(string service, string method, string path) =>
        "route." + service + "." + method.ToLowerInvariant() + "." +
        Convert.ToBase64String(Encoding.UTF8.GetBytes(path)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static ApprovedTarget? Resolve(string code, IConfiguration cfg, GatewayOptions gateway)
    {
        var parts = code.Split('.', 4);
        if (parts.Length != 4 || parts[0] != "route" || !SafeService.IsMatch(parts[1]) ||
            !Verbs.Contains(parts[2].ToUpperInvariant()) || parts[3].Length > 512)
            return null;
        string path;
        try
        {
            var data = parts[3].Replace('-', '+').Replace('_', '/');
            path = Encoding.UTF8.GetString(Convert.FromBase64String(data.PadRight((data.Length + 3) / 4 * 4, '=')));
        }
        catch (FormatException) { return null; }
        var method = parts[2].ToUpperInvariant();
        if (path.Length > 300 || !SafePath.IsMatch(path) ||
            path.Contains("..", StringComparison.Ordinal) ||
            !path.StartsWith("/api/" + parts[1] + "/", StringComparison.OrdinalIgnoreCase) ||
            !Services(gateway).Any(x => x.Service == parts[1]))
            return null;
        // Canonical strings are essential: never allow two spellings for the same target.
        if (Encode(parts[1], method, path) != code) return null;
        var required = Parameter.Matches(path).Select(m =>
            new ThirdPartyFieldOption(m.Groups[1].Value, "path." + m.Groups[1].Value,
                "Ruta: " + m.Groups[1].Value, true, true) { Group = "Parámetros de ruta" }).ToArray();
        return new ApprovedTarget(code, parts[1], method, path, "Ruta de servicio registrada; publicación sujeta a identidad técnica específica.")
        {
            Ready = !string.IsNullOrWhiteSpace(cfg["ThirdParty:ServiceTokens:" + code]),
            InputFields = required,
            OutputFields = [],
        };
    }

    public static ApprovedTarget? Resolve(RouteSelectionRequest request, IConfiguration cfg, GatewayOptions gateway)
    {
        if (request.Service is null || request.Method is null || request.Path is null) return null;
        var service = request.Service.Trim().ToLowerInvariant();
        var method = request.Method.Trim().ToUpperInvariant();
        var path = request.Path.Trim();
        return Resolve(Encode(service, method, path), cfg, gateway);
    }

    public static async Task<RouteDiscoveryResult> DiscoverAsync(
        string service, IConfiguration cfg, GatewayOptions gateway,
        IHttpClientFactory clients, string? adminAuthorization, CancellationToken ct)
    {
        var route = gateway.Routes.FirstOrDefault(r =>
            r.Prefix.Equals("/api/" + service, StringComparison.OrdinalIgnoreCase));
        if (route is null) return new(service, false, "Servicio sin ruta HTTP registrada en Gateway.", []);
        if (!Uri.TryCreate(route.Destination, UriKind.Absolute, out var root) ||
            root.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(root.UserInfo))
            return new(service, false, "Destino interno no permitido.", []);

        // Prefer the authenticated runtime endpoint registry. Unlike Swagger, it exists
        // in production and reflects the actually deployed minimal API route table.
        // The caller's admin JWT is forwarded ONLY to the fixed, registered service root.
        if (!string.IsNullOrWhiteSpace(adminAuthorization) &&
            adminAuthorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var internalUri = new Uri(root.GetLeftPart(UriPartial.Authority) + "/api/internal/third-party-route-catalog");
                using var req = new HttpRequestMessage(HttpMethod.Get, internalUri);
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", adminAuthorization[7..].Trim());
                using var response = await clients.CreateClient("third-party-upstream")
                    .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength is not > 2_000_000)
                {
                    var raw = await response.Content.ReadAsStringAsync(timeout.Token);
                    if (raw.Length <= 2_000_000 && JsonNode.Parse(raw) is JsonArray entries)
                    {
                        var discovered = new Dictionary<string, ApprovedTarget>(StringComparer.Ordinal);
                        foreach (var entry in entries.OfType<JsonObject>())
                        {
                            var method = entry["method"]?.ToString()?.ToUpperInvariant() ?? "";
                            var actualPath = entry["path"]?.ToString() ?? "";
                            if (!Verbs.Contains(method) || !actualPath.StartsWith('/')) continue;
                            string normalized;
                            var internalRoot = root.AbsolutePath.TrimEnd('/');
                            if (route.StripPrefix && internalRoot.Length > 0 &&
                                actualPath.StartsWith(internalRoot + "/", StringComparison.OrdinalIgnoreCase))
                                normalized = route.Prefix + actualPath[internalRoot.Length..];
                            else
                                normalized = actualPath;
                            var target = Resolve(new RouteSelectionRequest(service, method, normalized), cfg, gateway);
                            if (target is null) continue;
                            // Avoid advertising the catalogue itself, auth bootstrap, health probes and web sockets.
                            if (normalized.Contains("/third-party-route-catalog", StringComparison.OrdinalIgnoreCase) ||
                                normalized.StartsWith("/api/" + service + "/health", StringComparison.OrdinalIgnoreCase))
                                continue;
                            discovered[target.Code] = target with { Description = method + " " + normalized };
                        }
                        return new(service, true,
                            $"{discovered.Count} rutas HTTP detectadas en el servicio desplegado.",
                            discovered.Values.OrderBy(x => x.Path).ThenBy(x => x.Method).ToArray());
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
            {
                if (ct.IsCancellationRequested) throw;
            }
        }

        // Fixed well-known metadata URLs: no user-provided destination, redirect or credentials.
        foreach (var relative in new[] { "swagger/v1/swagger.json", "openapi/v1.json" })
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(root.ToString().TrimEnd('/') + "/" + relative));
                using var response = await clients.CreateClient("third-party-upstream")
                    .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > 2_000_000) continue;
                var raw = await response.Content.ReadAsStringAsync(timeout.Token);
                if (raw.Length > 2_000_000) continue;
                var document = JsonNode.Parse(raw);
                if (document?["paths"] is not JsonObject paths) continue;
                var found = new List<ApprovedTarget>();
                foreach (var (path, node) in paths)
                {
                    if (node is not JsonObject operations) continue;
                    var normalized = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
                        ? path : route.Prefix.TrimEnd('/') + (path.StartsWith('/') ? path : "/" + path);
                    foreach (var (verb, detail) in operations)
                    {
                        var method = verb.ToUpperInvariant();
                        if (!Verbs.Contains(method) || detail is not JsonObject operation) continue;
                        var target = Resolve(new RouteSelectionRequest(service, method, normalized), cfg, gateway);
                        if (target is null) continue;
                        var inputs = target.InputFields.ToList();
                        foreach (var collection in new[] { paths[path]?["parameters"] as JsonArray, operation["parameters"] as JsonArray })
                        {
                            if (collection is null) continue;
                            foreach (var p in collection.OfType<JsonObject>())
                            {
                                var name = p["name"]?.ToString() ?? "";
                                var where = p["in"]?.ToString() ?? "";
                                if (where is not ("query" or "path") || !Regex.IsMatch(name, @"^[a-zA-Z][a-zA-Z0-9_]*$") ||
                                    inputs.Any(f => f.Public == name)) continue;
                                inputs.Add(new ThirdPartyFieldOption(name, where + "." + name, name,
                                    p["required"]?.ToString() == "true", p["required"]?.ToString() == "true")
                                    { Group = where == "query" ? "Query" : "Ruta" });
                            }
                        }
                        if (method is "POST" or "PUT" or "PATCH" or "DELETE")
                            inputs.Add(new ThirdPartyFieldOption("body", "body", "Cuerpo JSON", false, false)
                            { Type = "JSON", Group = "Request" });
                        var outputs = new List<ThirdPartyFieldOption>();
                        var schema = operation["responses"]?["200"]?["content"]?["application/json"]?["schema"]
                                     ?? operation["responses"]?["201"]?["content"]?["application/json"]?["schema"];
                        if (schema is JsonObject schemaObj)
                        {
                            var resolved = schemaObj["$ref"]?.ToString();
                            if (!string.IsNullOrEmpty(resolved) && resolved.StartsWith("#/components/schemas/", StringComparison.Ordinal))
                                schema = document?["components"]?["schemas"]?[resolved["#/components/schemas/".Length..]];
                            if (schema?["items"] is JsonObject items)
                            {
                                var itemRef = items["$ref"]?.ToString();
                                schema = itemRef is not null && itemRef.StartsWith("#/components/schemas/", StringComparison.Ordinal)
                                    ? document?["components"]?["schemas"]?[itemRef["#/components/schemas/".Length..]] : items;
                            }
                            if (schema?["properties"] is JsonObject props)
                                foreach (var (key, _) in props)
                                    if (Regex.IsMatch(key, @"^[a-zA-Z][a-zA-Z0-9_]*$"))
                                        outputs.Add(new ThirdPartyFieldOption(key, key, key, false, true));
                        }
                        found.Add(target with {
                            Description = operation["summary"]?.ToString() ?? operation["operationId"]?.ToString() ?? normalized,
                            InputFields = inputs, OutputFields = outputs
                        });
                    }
                }
                return new(service, true, found.Count == 0 ? "Documento sin rutas HTTP compatibles." : "Rutas detectadas en OpenAPI.", found);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
            {
                if (ct.IsCancellationRequested) throw;
            }
        }
        return new(service, false,
            "El servicio no publica OpenAPI en este entorno. Podés registrar una ruta conocida manualmente como borrador; no se activa sin credencial específica.",
            []);
    }

    public static IReadOnlyList<string> PathNames(string path) =>
        Parameter.Matches(path).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();

    public static string? BuildPath(string template, IReadOnlyDictionary<string, string> values)
    {
        var missing = false;
        var result = Parameter.Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            if (!values.TryGetValue(name, out var value) ||
                string.IsNullOrWhiteSpace(value) || value.Length > 256 ||
                value is "." or ".." || value.Contains('/') || value.Contains('\\') ||
                value.Any(char.IsControl))
            { missing = true; return ""; }
            return Uri.EscapeDataString(value);
        });
        return missing || result.Contains('{') || result.Contains('}') ? null : result;
    }
}
