using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Dhole.ApiGateway.Realtime;

public sealed class RealtimeConnectionRegistry
{
    private sealed class Connection(ClaimsPrincipal principal, string bearer)
    {
        public ClaimsPrincipal Principal { get; } = principal;
        public string DelegatedBearer { get; } = bearer;
        public ConcurrentDictionary<RealtimeResource, byte> Subscriptions { get; } = new();
    }
    private readonly ConcurrentDictionary<string, Connection> _connections = new();

    public void Connect(string id, ClaimsPrincipal principal, string bearer)
    {
        if (principal.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(bearer))
            throw new UnauthorizedAccessException("Validated connection required.");
        _connections[id] = new Connection(principal, bearer);
    }

    public bool TrySubscribe(string id, RealtimeResource resource, int limit)
    {
        if (!_connections.TryGetValue(id, out var state)) return false;
        if (limit <= 0 || limit > 100) return false;
        if (state.Subscriptions.ContainsKey(resource)) return true;
        if (state.Subscriptions.Count >= limit) return false;
        return state.Subscriptions.TryAdd(resource, 0);
    }

    public bool Unsubscribe(string id, RealtimeResource resource) =>
        _connections.TryGetValue(id, out var state) &&
        state.Subscriptions.TryRemove(resource, out _);

    public void Disconnect(string id) => _connections.TryRemove(id, out _);

    public IReadOnlyList<RealtimeSubscriber> GetSubscribers(RealtimeResource resource) =>
        _connections.Where(x => x.Value.Subscriptions.ContainsKey(resource))
            .Select(x => new RealtimeSubscriber(x.Key, x.Value.Principal, x.Value.DelegatedBearer))
            .ToArray();

    public int ConnectionCount => _connections.Count;
}

public sealed record RealtimeSubscriber(string ConnectionId, ClaimsPrincipal User, string DelegatedBearer);

/// <summary>
/// Universal invalidation hub. No client-supplied groups, recipients or event
/// publishing. The resource owner authorizes each join; F3 bridge checks again
/// before EVERY delivery to handle revoked permissions.
/// </summary>
[Authorize]
public sealed class UniversalRealtimeHub(
    RealtimeConnectionRegistry registry,
    IRealtimeResourceAuthorizer access,
    IOptions<RealtimeHostOptions> options) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var bearer = Context.GetHttpContext()?.Items[
            RealtimeAuthentication.DelegatedTokenItem] as string;
        var principal = Context.User;
        if (string.IsNullOrWhiteSpace(bearer) ||
            principal?.Identity?.IsAuthenticated != true ||
            !HasGuidSubject(principal))
        {
            Context.Abort();
            return;
        }
        registry.Connect(Context.ConnectionId, principal, bearer);
        await base.OnConnectedAsync();
    }

    public async Task Subscribe(string resourceKind, string resourceId)
    {
        if (!RealtimeResourcePolicy.TryCreate(resourceKind, resourceId, out var resource))
            throw new HubException("Realtime subscription denied.");
        var ctx = Context.GetHttpContext();
        var bearer = ctx?.Items[RealtimeAuthentication.DelegatedTokenItem] as string;
        if (Context.User is not { Identity.IsAuthenticated: true } user ||
            string.IsNullOrEmpty(bearer) ||
            !await access.CanReadAsync(resource, user, bearer, Context.ConnectionAborted))
            throw new HubException("Realtime subscription denied.");

        if (!registry.TrySubscribe(
            Context.ConnectionId, resource, options.Value.MaxSubscriptionsPerConnection))
            throw new HubException("Realtime subscription limit exceeded.");
        await Groups.AddToGroupAsync(
            Context.ConnectionId, resource.GroupName, Context.ConnectionAborted);
    }

    public async Task Unsubscribe(string resourceKind, string resourceId)
    {
        if (!RealtimeResourcePolicy.TryCreate(resourceKind, resourceId, out var resource))
            throw new HubException("Realtime subscription denied.");
        registry.Unsubscribe(Context.ConnectionId, resource);
        await Groups.RemoveFromGroupAsync(
            Context.ConnectionId, resource.GroupName, Context.ConnectionAborted);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        registry.Disconnect(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public static bool HasGuidSubject(ClaimsPrincipal user) =>
        new[] {
            System.Security.Claims.ClaimTypes.NameIdentifier, "sub", "user_id", "userId"
        }.Any(key => Guid.TryParse(user.FindFirst(key)?.Value, out var id) && id != Guid.Empty);
}
