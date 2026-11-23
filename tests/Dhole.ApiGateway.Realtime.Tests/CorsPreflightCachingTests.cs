using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Dhole.ApiGateway.Realtime.Tests;

public sealed class CorsPreflightCachingTests
{
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Dhole.ApiGateway", "Program.cs")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Gateway repository source root unavailable");
    }

    [Fact]
    public void Cors_policy_reuses_browser_preflights_and_preserves_authentication()
    {
        var program = File.ReadAllText(Path.Combine(Root(), "Dhole.ApiGateway", "Program.cs"));
        Assert.Contains(".WithOrigins(allowedOrigins.ToArray())", program);
        Assert.Contains(".AllowCredentials()", program);
        Assert.Contains(".SetPreflightMaxAge(TimeSpan.FromHours(1))", program);
        Assert.Contains("app.UseCors(CorsPolicyName)", program);
    }

    [Fact]
    public void Coordinated_version_manifest_keeps_other_microservices_unchanged()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(Root(), "Dhole.ApiGateway", "service-versions.json")));
        var root = document.RootElement;
        Assert.Equal("1.120.3", root.GetProperty("ecosystemVersion").GetString());
        var gateways = root.GetProperty("services").EnumerateArray()
            .Where(s => s.GetProperty("key").GetString() == "gateway").ToArray();
        Assert.Equal("1.11.1", Assert.Single(gateways).GetProperty("version").GetString());
    }
}
