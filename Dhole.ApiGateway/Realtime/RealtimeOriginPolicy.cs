using Microsoft.Extensions.Configuration;

namespace Dhole.ApiGateway.Realtime;

/// <summary>
/// CORS does not protect WebSocket upgrades. Browser-origin checks supplement
/// single-use tickets and validated JWTs, not replace authentication.
/// </summary>
public static class RealtimeOriginPolicy
{
    public static bool IsAllowed(string? rawOrigin, IConfiguration configuration)
    {
        if (!TryNormalize(rawOrigin, out var origin)) return false;
        var allowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var configured in configuration.GetSection("Cors:AllowedOrigins")
                     .Get<string[]>() ?? Array.Empty<string>())
        {
            if (TryNormalize(configured, out var allowed)) allowlist.Add(allowed);
        }

        foreach (var key in new[] { "DHOLE_WEB_HOST", "VITE_FRONTEND_DOMAIN" })
        {
            var host = configuration[key]?.Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(host)) continue;
            if (TryNormalize(host, out var parsed)) allowlist.Add(parsed);
            else if (!host.Contains("://", StringComparison.Ordinal) &&
                     !host.Contains('/') && !host.Contains('@'))
            {
                if (TryNormalize("https://" + host, out parsed)) allowlist.Add(parsed);
            }
        }

        return allowlist.Contains(origin);
    }

    private static bool TryNormalize(string? raw, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(raw) ||
            !Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment)) return false;
        normalized = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return true;
    }
}
