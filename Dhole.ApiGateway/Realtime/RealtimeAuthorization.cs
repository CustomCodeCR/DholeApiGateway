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
    // Only backend-confirmed resources. Adding a resource requires a reviewed
    // owner REST GET with authorization, an ADR and negative security tests.
    public static bool TryCreate(string? kind, string? rawId, out RealtimeResource resource)
    {
        resource = default;
        if (kind is not ("crm.account" or "pricing.rate") ||
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
        // The producer cannot designate a SignalR group or arbitrary recipient.
        var segments = reference.Split(':');
        if (segments.Length != 3) return false;
        string? kind = (segments[0], segments[1]) switch
        {
            ("crm", "account") => "crm.account",
            ("pricing", "rate") => "pricing.rate",
            _ => null
        };
        return TryCreate(kind, segments[2], out resource);
    }

    public static string? ServiceFor(string kind) => kind switch
    {
        "crm.account" => "crm",
        "pricing.rate" => "pricing",
        _ => null
    };

    public static string? PathFor(RealtimeResource resource) => resource.Kind switch
    {
        "crm.account" => $"/api/crm/accounts/{resource.Id:D}",
        "pricing.rate" => $"/api/pricing/rates/{resource.Id:D}",
        _ => null
    };
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
            origin.Scheme != Uri.UriSchemeHttp ||
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
