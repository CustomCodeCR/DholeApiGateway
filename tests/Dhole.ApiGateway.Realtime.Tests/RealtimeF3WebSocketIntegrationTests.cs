using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Dhole.ApiGateway.Realtime;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using Xunit;

namespace Dhole.ApiGateway.Realtime.Tests;

public sealed class RealtimeF3WebSocketIntegrationTests
{
    private const string TestKey = "ci-only-rt-f3-gateway-jwt-signing-key-64-bytes-please-never-deploy-01234";
    private static readonly Guid IdA = Guid.Parse("aaaa1111-1111-4111-8111-111111111111");
    private static readonly Guid IdB = Guid.Parse("bbbb2222-2222-4222-8222-222222222222");
    private static readonly Guid IdDenied = Guid.Parse("cccc3333-3333-4333-8333-333333333333");
    private static readonly Guid ResourceId = Guid.Parse("dddd4444-4444-4444-8444-444444444444");

    private sealed class TestResourceAuthorizer : IRealtimeResourceAuthorizer
    {
        private readonly HashSet<Guid> _allowed = new() { IdA, IdB };
        public void Revoke(Guid id) { lock (_allowed) _allowed.Remove(id); }

        public Task<bool> CanReadAsync(RealtimeResource resource, ClaimsPrincipal user,
            string? delegatedBearer, CancellationToken ct)
        {
            var identity = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst("sub")?.Value;
            var validId = Guid.TryParse(identity, out var id);
            lock (_allowed)
                return Task.FromResult(validId &&
                    _allowed.Contains(id) && resource.Kind == "crm.account" &&
                    resource.Id == ResourceId && !string.IsNullOrWhiteSpace(delegatedBearer));
        }
    }

    private static string MakeJwt(Guid id)
    {
        var credential = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestKey)),
            SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            issuer: "DholeAuthService", audience: "DholeEcosystem",
            claims: new[] { new Claim(JwtRegisteredClaimNames.Sub, id.ToString("D")) },
            notBefore: DateTime.UtcNow.AddSeconds(-5),
            expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: credential);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static async Task<string> ReceiveUntil(
        WebSocket socket, Func<JsonElement, bool> predicate,
        TimeSpan? timeout = null)
    {
        using var cancel = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(15));
        var buffer = new byte[16384];
        var pending = new StringBuilder();
        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancel.Token);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException("Realtime WebSocket closed before expected message.");
            pending.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            var text = pending.ToString();
            var lastSeparator = text.LastIndexOf('\u001e');
            if (lastSeparator < 0) continue;
            pending.Clear();
            pending.Append(text[(lastSeparator + 1)..]);
            foreach (var message in text[..lastSeparator].Split('\u001e',
                StringSplitOptions.RemoveEmptyEntries))
            {
                using var doc = JsonDocument.Parse(message);
                if (predicate(doc.RootElement)) return message;
            }
        }
    }

    private static async Task SendHub(WebSocket socket, string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload + "\u001e");
        await socket.SendAsync(new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<WebSocket> ConnectAuthenticatedAsync(
        WebApplicationFactory<Program> factory, Guid userId)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/hubs/realtime/ticket");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MakeJwt(userId));
        using var response = await client.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode,
            $"Ticket POST rejected with {response.StatusCode}");
        using var ticketBody = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ticket = ticketBody.RootElement.GetProperty("ticket").GetString();
        Assert.NotNull(ticket);
        var wsClient = factory.Server.CreateWebSocketClient();
        wsClient.ConfigureRequest = msg => msg.Headers["Origin"] = "http://localhost";
        var socket = await wsClient.ConnectAsync(
            new Uri("ws://localhost/hubs/realtime?ticket=" + ticket),
            CancellationToken.None);
        await SendHub(socket, """{"protocol":"json","version":1}""");
        Assert.Equal("{}", await ReceiveUntil(socket, _ => true));
        return socket;
    }

    private static async Task<string> Subscribe(WebSocket socket, int invocation)
    {
        await SendHub(socket, JsonSerializer.Serialize(new {
            type = 1, invocationId = invocation.ToString(), target = "Subscribe",
            arguments = new[] { "crm.account", ResourceId.ToString("D") }
        }));
        return await ReceiveUntil(socket, root =>
            root.TryGetProperty("type", out var type) && type.GetInt32() == 3 &&
            root.TryGetProperty("invocationId", out var id) &&
            id.GetString() == invocation.ToString());
    }

    [Fact]
    public async Task Two_valid_users_receive_stream_invalidation_denied_user_does_not_and_revocation_is_checked()
    {
        var redisConnection = Environment.GetEnvironmentVariable("F3_REDIS_TEST")
            ?? throw new InvalidOperationException("F3_REDIS_TEST required.");
        var postgres = Environment.GetEnvironmentVariable("F3_POSTGRES_TEST")
            ?? throw new InvalidOperationException("F3_POSTGRES_TEST required.");
        var instance = "ci-f3-" + Guid.NewGuid().ToString("N");
        var access = new TestResourceAuthorizer();

        using var application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
            {
                host.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Features:UniversalRealtimeEnabled"] = "true",
                        ["Realtime:Enabled"] = "true",
                        ["Realtime:BatchApproved"] = "true", // CI-only, never deployed
                        ["Realtime:ReleaseId"] = "rt-20261107-01",
                        ["Realtime:BridgeEnabled"] = "true",
                        ["Realtime:RedisConnectionString"] = redisConnection,
                        ["Realtime:InboxPostgresConnectionString"] = postgres,
                        ["Realtime:InstanceId"] = instance,
                        ["Auth:Jwt:Issuer"] = "DholeAuthService",
                        ["Auth:Jwt:Audience"] = "DholeEcosystem",
                        ["Auth:Jwt:SecretKey"] = TestKey,
                        ["Cors:AllowedOrigins:0"] = "http://localhost"
                    }));
                host.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IRealtimeResourceAuthorizer>();
                    services.AddSingleton<IRealtimeResourceAuthorizer>(access);
                });
            });
        using var a = await ConnectAuthenticatedAsync(application, IdA);
        using var b = await ConnectAuthenticatedAsync(application, IdB);
        using var denied = await ConnectAuthenticatedAsync(application, IdDenied);

        Assert.DoesNotContain("error", await Subscribe(a, 1), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error", await Subscribe(b, 2), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("error", await Subscribe(denied, 3), StringComparison.OrdinalIgnoreCase);

        var registry = application.Services.GetRequiredService<RealtimeConnectionRegistry>();
        Assert.Equal(2, registry.GetSubscribers(new("crm.account", ResourceId)).Count);

        using var redis = await ConnectionMultiplexer.ConnectAsync(redisConnection);
        var db = redis.GetDatabase();
        async Task Publish(Guid id)
        {
            var json = JsonSerializer.Serialize(new {
                eventId = id,
                eventType = "crm.account.updated.v1",
                schemaVersion = 1,
                aggregateType = "account",
                aggregateId = ResourceId.ToString("D"),
                aggregateVersion = 1,
                occurredAtUtc = DateTimeOffset.UtcNow,
                producer = "DholeCRMService",
                correlationId = "f3-ci",
                audienceRef = $"crm:account:{ResourceId:D}",
                payload = new { aggregateVersion = 1 }
            });
            await db.StreamAddAsync(RealtimeStreamBridge.CrmStream, new[]
            {
                new NameValueEntry("event_id", id.ToString("D")),
                new NameValueEntry("event_type", "crm.account.updated.v1"),
                new NameValueEntry("payload_json", json)
            });
        }

        var first = Guid.NewGuid();
        await Publish(first);
        var receivedA = await ReceiveUntil(a, root =>
            root.TryGetProperty("target", out var target) &&
            target.GetString() == "resourceChanged");
        var receivedB = await ReceiveUntil(b, root =>
            root.TryGetProperty("target", out var target) &&
            target.GetString() == "resourceChanged");
        Assert.Contains(first.ToString("D"), receivedA, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(first.ToString("D"), receivedB, StringComparison.OrdinalIgnoreCase);

        access.Revoke(IdB);
        var second = Guid.NewGuid();
        await Publish(second);
        var afterRevoke = await ReceiveUntil(a, root =>
            root.TryGetProperty("target", out var target) &&
            target.GetString() == "resourceChanged");
        Assert.Contains(second.ToString("D"), afterRevoke, StringComparison.OrdinalIgnoreCase);
        Assert.Single(registry.GetSubscribers(new("crm.account", ResourceId)));
    }
}
