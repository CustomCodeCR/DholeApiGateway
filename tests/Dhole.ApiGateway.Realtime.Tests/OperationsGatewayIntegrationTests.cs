using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Dhole.ApiGateway.Realtime.Tests;

public sealed class OperationsGatewayIntegrationTests
{
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Dhole.ApiGateway", "appsettings.json")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Repository source root unavailable");
    }

    [Fact]
    public void Operations_http_and_signalr_routes_keep_full_paths()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "Dhole.ApiGateway", "appsettings.json")));
        var routes = doc.RootElement.GetProperty("Gateway").GetProperty("Routes").EnumerateArray().ToArray();
        var operations = Assert.Single(routes.Where(r => r.GetProperty("Prefix").GetString() == "/api/operations"));
        var hub = Assert.Single(routes.Where(r => r.GetProperty("Prefix").GetString() == "/hubs/operations"));
        Assert.Equal("http://operations-api:8080", operations.GetProperty("Destination").GetString());
        Assert.Equal("http://operations-api:8080", hub.GetProperty("Destination").GetString());
        Assert.False(operations.GetProperty("StripPrefix").GetBoolean());
        Assert.False(hub.GetProperty("StripPrefix").GetBoolean());
        Assert.Equal("http://operations-api:8080",
            doc.RootElement.GetProperty("Gateway").GetProperty("HealthChecks").GetProperty("operations").GetString());
    }

    [Fact]
    public void Operations_registered_version_is_unique_and_semver()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "Dhole.ApiGateway", "service-versions.json")));
        var services = doc.RootElement.GetProperty("services").EnumerateArray().ToArray();
        var operations = Assert.Single(services.Where(s => s.GetProperty("key").GetString() == "operations"));
        Assert.Equal("DholeOperationsService", operations.GetProperty("component").GetString());
        Assert.Equal("0.3.1", operations.GetProperty("version").GetString());
    }
}
