using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using StackExchange.Redis;

namespace Dhole.ApiGateway.Realtime;

/// <summary>
/// Browser WebSocket upgrade uses only an opaque single-use 60s ticket.
/// The JWT is exchanged over HTTPS POST Authorization and never put in URL.
/// Redis GETDEL is atomic across multiple Gateway instances.
/// </summary>
public sealed class RealtimeAccessTicketStore(IConnectionMultiplexer redis)
{
    public const int LifetimeSeconds = 60;
    private const string Prefix = "dhole:rt:v1:ticket:";

    public async Task<string> IssueAsync(string bearer, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(bearer) || bearer.Length > 8192)
            throw new ArgumentException("Validated bearer required.", nameof(bearer));
        var ticket = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var accepted = await redis.GetDatabase().StringSetAsync(
            Prefix + ticket, bearer, TimeSpan.FromSeconds(LifetimeSeconds),
            When.NotExists);
        if (!accepted) throw new InvalidOperationException("Could not allocate realtime ticket.");
        return ticket;
    }

    public async Task<string?> RedeemAsync(string? ticket, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (ticket is null || ticket.Length != 43 || ticket.Any(c =>
            !((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
              (c >= '0' && c <= '9') || c is '-' or '_')))
            return null;
        var value = await redis.GetDatabase().StringGetDeleteAsync(Prefix + ticket);
        return value.IsNullOrEmpty ? null : value.ToString();
    }
}

/// <summary>
/// Per-connection token is used ONLY to revalidate permissions with the
/// resource owner. It is stored in Hub Context.Items, never sent to browsers
/// or logs. Cleared on disconnect.
/// </summary>
public static class RealtimeAuthentication
{
    public const string DelegatedTokenItem = "DholeRealtime:DelegatedBearer";
}
