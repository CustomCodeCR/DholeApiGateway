using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Dhole.ApiGateway.Gateway;

namespace Dhole.ApiGateway.Realtime;

public sealed class RealtimeHostOptions
{
    public const string Section = "Realtime";
    public bool Enabled { get; set; } = false;
    public bool BridgeEnabled { get; set; } = false;
    public bool PricingBridgeEnabled { get; set; } = false; // F6.02 opt-in, OFF by default
    public bool UniversalServicesBridgeEnabled { get; set; } = false; // F6.04-13, OFF by default
    public bool BatchApproved { get; set; } = false;
    public string ReleaseId { get; set; } = "";
    public string RedisConnectionString { get; set; } = "";
    public string InboxPostgresConnectionString { get; set; } = "";
    public string InstanceId { get; set; } = "";
    public int MaxSubscriptionsPerConnection { get; set; } = 25;
}

public readonly record struct RealtimeResource(string Kind, Guid Id)
{
    public string Key => $"{Kind}:{Id:N}";
    public string GroupName => $"dhole:rt:{Key}";
}

public static class RealtimeResourcePolicy
{
    // Owner REST GET (or narrowly scoped access check) MUST be reviewed,
    // authentication required, and 401/403/404 treated as denial. No tenant or
    // role names are ever derived from audienceRef; only allowlisted resources.
    private static readonly IReadOnlyDictionary<string, (string Service, string Path)> Allowlist =
        new Dictionary<string, (string Service, string Path)>(StringComparer.Ordinal)
        {
            ["crm.account"] = ("crm", "/api/crm/accounts/{id}"),
            ["pricing.rate"] = ("pricing", "/api/pricing/rates/{id}"),
            ["auth.user"] = ("auth", "/api/auth/users/{id}/realtime-access"),
            ["config.catalog-group"] = ("config", "/api/config/catalog-groups/{id}"),
            ["config.catalog-item"] = ("config", "/api/config/catalog-items/{id}"),
            ["srm.supplier"] = ("config", "/api/config/srm/suppliers/{id}/realtime-access"),
            ["audit.event"] = ("auditlogs", "/api/auditlogs/events/{id}"),
            ["storage.file"] = ("storage", "/api/v1/storage/files/{id}"),
            ["extraction.execution"] = ("data-extraction", "/api/data-extraction/realtime-access/execution/{id}"),
            ["extraction.emailjob"] = ("data-extraction", "/api/data-extraction/realtime-access/emailjob/{id}"),
            ["extraction.airequest"] = ("data-extraction", "/api/data-extraction/realtime-access/airequest/{id}"),
            ["reports.template"] = ("reports", "/api/reports/templates/{id}"),
            ["dynamics.job"] = ("dynamics", "/api/admin/v1/operations/realtime/jobs/{id}"),
            ["ai.execution"] = ("ai", "/api/ai/executions/{id}"),
            ["ai.emailjob"] = ("ai", "/api/ai/realtime-access/emailjob/{id}"),
            ["agent.execution"] = ("agent", "/api/agents/executions/{id}"),
            ["agent.definition"] = ("agent", "/api/agents/definitions/{id}"),
            ["agent.schedule"] = ("agent", "/api/agents/schedules/{id}"),
            ["content.item"] = ("content", "/api/content/items/{id}"),
            ["content.menu"] = ("content", "/api/content/realtime-access/menu/{id}"),
            ["content.setting"] = ("content", "/api/content/realtime-access/setting/{id}")
        };

    public static bool TryCreate(string? kind, string? rawId, out RealtimeResource resource)
    {
        resource = default;
        if (kind is null || !Allowlist.ContainsKey(kind) ||
            !Guid.TryParse(rawId, out var parsed) || parsed == Guid.Empty)
            return false;
        resource = new RealtimeResource(kind, parsed);
        return true;
    }

    public static bool TryFromAudienceRef(string? reference, out RealtimeResource resource)
    {
        resource = default;
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 160)
            return false;
        var segments = reference.Split(':');
        if (segments.Length != 3) return false;
        return TryCreate(segments[0] + "." + segments[1], segments[2], out resource);
    }

    public static string? ServiceFor(string kind) =>
        Allowlist.TryGetValue(kind, out var owner) ? owner.Service : null;

    public static string? PathFor(RealtimeResource resource) =>
        Allowlist.TryGetValue(resource.Kind, out var owner)
            ? owner.Path.Replace("{id}", resource.Id.ToString("D"), StringComparison.Ordinal)
            : null;
}

/// <summary>
/// Every subscription and every delivery is authorized by the owning REST
/// service. HTTP 200 alone permits; 401/403/404, errors and timeouts deny.
/// The destination is from server configuration, never user-controlled.
/// </summary>
public interface IRealtimeResourceAuthorizer
{
    Task<bool> CanReadAsync(
        RealtimeResource resource, ClaimsPrincipal user,
        string? delegatedBearer, CancellationToken ct);
}

public sealed class OwnerRestRealtimeResourceAuthorizer(
    IHttpClientFactory clientFactory, IOptions<GatewayOptions> gatewayOptions)
    : IRealtimeResourceAuthorizer
{
    public async Task<bool> CanReadAsync(
        RealtimeResource resource, ClaimsPrincipal user,
        string? delegatedBearer, CancellationToken ct)
    {
        if (user.Identity?.IsAuthenticated != true ||
            string.IsNullOrWhiteSpace(delegatedBearer)) return false;

        var service = RealtimeResourcePolicy.ServiceFor(resource.Kind);
        var route = RealtimeResourcePolicy.PathFor(resource);
        if (service is null || route is null ||
            !gatewayOptions.Value.HealthChecks.TryGetValue(service, out var destination) ||
            !Uri.TryCreate(destination, UriKind.Absolute, out var origin) ||
            origin.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(origin.UserInfo)) return false;
        // URI paths never interpolate arbitrary user text: resource IDs are Guid.
        var url = new Uri(origin.GetLeftPart(UriPartial.Authority) + route);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", delegatedBearer);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var response = await clientFactory.CreateClient("realtime-acl")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false; // never fail open on network/auth errors
        }
    }
}
