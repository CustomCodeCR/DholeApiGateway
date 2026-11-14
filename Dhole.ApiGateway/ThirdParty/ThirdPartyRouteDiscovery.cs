using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dhole.ApiGateway.Gateway;
using Microsoft.Extensions.Options;

namespace Dhole.ApiGateway.ThirdParty;

public sealed record DholeRouteStatus(string Service, string Prefix, string Status, int Routes, string? Message);
public sealed record DholeRouteDiscoveryResult(IReadOnlyList<ApprovedTarget> Targets, IReadOnlyList<DholeRouteStatus> Services);

/// <summary>
/// Discovers actual REST endpoints through authenticated manifests on registered upstreams.
/// No arbitrary target URI can be supplied by the browser or an external API consumer.
/// </summary>
public static class ThirdPartyRouteDiscovery
{
    private sealed record ManifestParameter(string Name, bool Required);
    private sealed record ManifestRoute(string Method, string Path, string Description,
        List<ManifestParameter>? Parameters);
    private sealed record RouteManifest(int SchemaVersion, List<ManifestRoute>? Routes);
    private sealed record DiscoveryBatch(List<ApprovedTarget> Targets, DholeRouteStatus Status);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex Parameter = new(@"\{([a-zA-Z][a-zA-Z0-9_]*)(?::[^{}]+)?\??\}",
        RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedMethods = new(StringComparer.OrdinalIgnoreCase)
        { "GET", "POST", "PUT", "DELETE" };
    private static readonly HashSet<string> SensitiveSegments = new(StringComparer.OrdinalIgnoreCase)
        { "admin", "internal", "auth", "user", "users", "sessions", "roles", "scopes",
          "permissions", "secrets", "credentials", "tokens", "keys", "maintenance",
          "environment", "recovery", "bootstrap", "system", "audit", "health", "ready",
          "debug", "diagnostics", "impersonation", "realtime-access", "reset-password" };

    public static async Task<DholeRouteDiscoveryResult> DiscoverAsync(
        HttpContext ctx, IConfiguration cfg, IOptions<GatewayOptions> options,
        IHttpClientFactory factory)
    {
        // The administrator JWT is forwarded solely to known internal services for discovery.
        // Public API Key requests never call this method.
        var bearer = ctx.Request.Headers.Authorization.ToString();
        if (!AuthenticationHeaderValue.TryParse(bearer, out var auth) ||
            !string.Equals(auth.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(auth.Parameter))
            return new DholeRouteDiscoveryResult([], []);

        var routes = options.Value.Routes
            .Where(r => r.Prefix.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => r.Prefix, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();
        var tasks = routes.Select(route => LoadAsync(route, cfg, factory, auth.Parameter!, ctx.RequestAborted));
        var batches = await Task.WhenAll(tasks);
        return new DholeRouteDiscoveryResult(
            batches.SelectMany(batch => batch.Targets).DistinctBy(target => target.Code).ToArray(),
            batches.Select(batch => batch.Status).ToArray());
    }

    private static async Task<DiscoveryBatch> LoadAsync(
        GatewayRoute route, IConfiguration cfg, IHttpClientFactory factory,
        string bearer, CancellationToken ct)
    {
        var service = route.Prefix["/api/".Length..].ToLowerInvariant();
        try
        {
            if (!Uri.TryCreate(route.Destination, UriKind.Absolute, out var destination) ||
                destination.Scheme is not ("http" or "https"))
                return Failed("invalid_destination");
            var origin = destination.GetLeftPart(UriPartial.Authority);
            using var request = new HttpRequestMessage(HttpMethod.Get, origin + "/.well-known/dhole/routes");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            using var response = await factory.CreateClient("third-party-discovery")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return Failed("not_installed");
            if (!response.IsSuccessStatusCode)
                return Failed(response.StatusCode is System.Net.HttpStatusCode.Forbidden or
                    System.Net.HttpStatusCode.Unauthorized ? "unauthorized" : "unavailable");
            if (response.Content.Headers.ContentLength is > 2_000_000)
                return Failed("manifest_too_large");
            var text = await response.Content.ReadAsStringAsync(ct);
            if (text.Length > 2_000_000)
                return Failed("manifest_too_large");
            var manifest = JsonSerializer.Deserialize<RouteManifest>(text, Json);
            if (manifest?.SchemaVersion != 1 || manifest.Routes is null)
                return Failed("invalid_manifest");

            var list = new List<ApprovedTarget>();
            foreach (var item in manifest.Routes.Take(4000))
            {
                if (!AllowedMethods.Contains(item.Method) ||
                    !item.Path.StartsWith("/", StringComparison.Ordinal) ||
                    item.Path.Contains("..", StringComparison.Ordinal) ||
                    item.Path.Contains('?') || item.Path.Contains('#') ||
                    item.Path.Contains("//", StringComparison.Ordinal))
                    continue;
                var path = Parameter.Replace(item.Path, "{$1}");
                if (path.Contains('{') && !Regex.IsMatch(path, @"^(?:[^{}]|\{[A-Za-z][A-Za-z0-9_]*\})+$"))
                    continue;
                var mapped = PublicPath(route, destination, path);
                var routable = mapped.StartsWith(route.Prefix + "/", StringComparison.OrdinalIgnoreCase) ||
                               mapped.Equals(route.Prefix, StringComparison.OrdinalIgnoreCase);
                var publishable = routable && !Sensitive(mapped);
                var code = "route-" + Regex.Replace(service, "[^a-z0-9-]", "") + "-" +
                    item.Method.ToLowerInvariant() + "-" +
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(mapped)))[..12].ToLowerInvariant();
                var token = cfg["ThirdParty:ServiceTokens:" + code] ??
                            cfg["ThirdParty:ServiceTokens:Service:" + service];
                var parameters = item.Parameters ?? [];
                var input = parameters
                    .Where(p => Regex.IsMatch(p.Name, "^[A-Za-z][A-Za-z0-9_]*$"))
                    .Select(p => new ThirdPartyFieldOption(p.Name, "path." + p.Name, p.Name,
                        p.Required, p.Required) { Type = "texto", Group = "Ruta" })
                    .ToList();
                if (!string.Equals(item.Method, "GET", StringComparison.OrdinalIgnoreCase))
                    input.Add(new ThirdPartyFieldOption("payload", "body", "Cuerpo JSON completo")
                        { Type = "JSON", Group = "Body" });
                list.Add(new ApprovedTarget(code, service, item.Method.ToUpperInvariant(), mapped,
                    string.IsNullOrWhiteSpace(item.Description) ? mapped : item.Description)
                {
                    Discovered = true,
                    Publishable = publishable,
                    Ready = publishable && !string.IsNullOrWhiteSpace(token),
                    InputFields = input,
                    OutputFields = [new ThirdPartyFieldOption("respuesta", "value", "Respuesta completa")
                        { Type = "JSON", Group = "Respuesta" }],
                    RejectionReason = publishable ? null : "Ruta interna, administrativa o sin proxy público"
                });
            }
            return new DiscoveryBatch(list, new DholeRouteStatus(service, route.Prefix,
                "available", list.Count, null));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Failed("unreachable");
        }

        DiscoveryBatch Failed(string reason) =>
            new([], new DholeRouteStatus(service, route.Prefix, reason, 0,
                "No se pudo consultar el manifiesto del servicio."));
    }

    private static string PublicPath(GatewayRoute route, Uri destination, string path)
    {
        if (!route.StripPrefix) return path;
        var basePath = destination.AbsolutePath.TrimEnd('/');
        if (basePath.Length > 0 && path.StartsWith(basePath + "/", StringComparison.OrdinalIgnoreCase))
            return route.Prefix.TrimEnd('/') + path[basePath.Length..];
        return route.Prefix.TrimEnd('/') + path;
    }

    private static bool Sensitive(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(SensitiveSegments.Contains);
}
