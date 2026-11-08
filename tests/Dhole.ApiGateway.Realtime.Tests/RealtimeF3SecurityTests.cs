using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Dhole.ApiGateway.Gateway;
using Dhole.ApiGateway.Realtime;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace Dhole.ApiGateway.Realtime.Tests;

public sealed class RealtimeF3SecurityTests
{
    private static readonly Guid AccountId = Guid.Parse("12345678-1111-4111-8111-111111111111");
    private static readonly Guid PricingId = Guid.Parse("12345678-2222-4222-8222-222222222222");

    private static ClaimsPrincipal User(Guid id) =>
        new(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString("D")) }, "test-jwt"));

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> answer)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private static OwnerRestRealtimeResourceAuthorizer Authorizer(
        FakeHandler handler, string address = "http://crm-internal:8080")
    {
        var client = new HttpClient(handler);
        return new OwnerRestRealtimeResourceAuthorizer(new Factory(client),
            Options.Create(new GatewayOptions
            {
                HealthChecks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["crm"] = address,
                    ["pricing"] = "http://pricing-internal:8080"
                }
            }));
    }

    private static string Envelope(Guid eventId, Guid accountId, string? audience = null,
        string eventType = "crm.account.updated.v1",
        int schemaVersion = 1, long version = 1) =>
        JsonSerializer.Serialize(new
        {
            eventId, eventType, schemaVersion,
            aggregateType = "account",
            aggregateId = accountId.ToString("D"),
            aggregateVersion = version,
            occurredAtUtc = DateTimeOffset.UtcNow,
            producer = "DholeCRMService",
            correlationId = "f3-test",
            audienceRef = audience ?? $"crm:account:{accountId:D}",
            payload = new { aggregateVersion = version }
        });

    [Fact]
    public void Resource_group_generated_from_server_known_guid() =>
        Assert.Equal($"dhole:rt:crm.account:{AccountId:N}",
            new RealtimeResource("crm.account", AccountId).GroupName);

    [Theory]
    [InlineData("crm.account", "12345678-1111-4111-8111-111111111111", true)]
    [InlineData("pricing.rate", "12345678-1111-4111-8111-111111111111", true)]
    [InlineData("crm.account", "00000000-0000-0000-0000-000000000000", false)]
    [InlineData("crm.account", "fake", false)]
    [InlineData("crm.account", "", false)]
    [InlineData("crm.account", "12345678-1111-4111-8111-111111111111?extra", false)]
    [InlineData("CRM.ACCOUNT", "12345678-1111-4111-8111-111111111111", false)]
    [InlineData("crm.accounts", "12345678-1111-4111-8111-111111111111", false)]
    [InlineData("system.users", "12345678-1111-4111-8111-111111111111", false)]
    [InlineData("notifications.user", "12345678-1111-4111-8111-111111111111", false)]
    [InlineData("user:all", "12345678-1111-4111-8111-111111111111", false)]
    [InlineData("tenant", "12345678-1111-4111-8111-111111111111", false)]
    public void Resource_allowlist_cannot_be_influenced_by_group_input(
        string kind, string id, bool expected) =>
        Assert.Equal(expected, RealtimeResourcePolicy.TryCreate(kind, id, out _));

    [Theory]
    [InlineData("crm:account:12345678-1111-4111-8111-111111111111", true)]
    [InlineData("pricing:rate:12345678-2222-4222-8222-222222222222", true)]
    [InlineData("user:all:12345678-1111-4111-8111-111111111111", false)]
    [InlineData("crm:account:00000000-0000-0000-0000-000000000000", false)]
    [InlineData("crm:account:abc", false)]
    [InlineData("crm:admin:12345678-1111-4111-8111-111111111111", false)]
    [InlineData("pricing:quote:12345678-1111-4111-8111-111111111111", false)]
    [InlineData("crm:account:12345678-1111-4111-8111-111111111111:all", false)]
    [InlineData("user:12345678-1111-4111-8111-111111111111", false)]
    [InlineData("", false)]
    public void Audience_ref_cannot_designate_an_arbitrary_hub_group(
        string value, bool expected) =>
        Assert.Equal(expected, RealtimeResourcePolicy.TryFromAudienceRef(value, out _));

    [Fact]
    public void Bridge_parses_only_minimal_authenticated_resource_invalidation()
    {
        var eventId = Guid.NewGuid();
        Assert.True(RealtimeStreamBridge.TryParseMinimal(
            Envelope(eventId, AccountId), eventId, out var message, out var resource));
        Assert.Equal(eventId, message.EventId);
        Assert.Equal(AccountId, resource.Id);
        Assert.Equal("crm.account", resource.Kind);
        Assert.Equal(1L, message.AggregateVersion);
    }

    [Fact]
    public void Mismatched_event_id_is_rejected()
    {
        var id = Guid.NewGuid();
        Assert.False(RealtimeStreamBridge.TryParseMinimal(
            Envelope(id, AccountId), Guid.NewGuid(), out _, out _));
    }

    [Fact]
    public void Mismatched_aggregate_and_audience_is_rejected()
    {
        var id = Guid.NewGuid();
        Assert.False(RealtimeStreamBridge.TryParseMinimal(
            Envelope(id, AccountId, $"crm:account:{PricingId:D}"), id, out _, out _));
    }

    [Fact]
    public void Invalid_event_type_must_not_cross_service_resource()
    {
        var id = Guid.NewGuid();
        Assert.False(RealtimeStreamBridge.TryParseMinimal(
            Envelope(id, AccountId, eventType: "pricing.rate.updated.v1"), id, out _, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Schema_must_be_positive(int version)
    {
        var id = Guid.NewGuid();
        Assert.False(RealtimeStreamBridge.TryParseMinimal(
            Envelope(id, AccountId, schemaVersion: version), id, out _, out _));
    }

    [Fact]
    public void Aggregate_revision_must_be_positive()
    {
        var id = Guid.NewGuid();
        Assert.False(RealtimeStreamBridge.TryParseMinimal(
            Envelope(id, AccountId, version: 0), id, out _, out _));
    }

    [Fact]
    public void Browser_cannot_subscribe_by_impersonating_another_resource_group()
    {
        var registry = new RealtimeConnectionRegistry();
        registry.Connect("connection-a", User(Guid.NewGuid()), "valid-a");
        registry.Connect("connection-b", User(Guid.NewGuid()), "valid-b");
        Assert.True(registry.TrySubscribe("connection-a",
            new RealtimeResource("crm.account", AccountId), 25));
        Assert.True(registry.TrySubscribe("connection-b",
            new RealtimeResource("crm.account", PricingId), 25));
        var audience = registry.GetSubscribers(new RealtimeResource("crm.account", AccountId));
        var only = Assert.Single(audience);
        Assert.Equal("connection-a", only.ConnectionId);
        Assert.DoesNotContain(audience, x => x.ConnectionId == "connection-b");
        registry.Disconnect("connection-a");
        Assert.Empty(registry.GetSubscribers(new RealtimeResource("crm.account", AccountId)));
    }

    [Fact]
    public void Subscription_limit_enforced_without_mutating_existing_groups()
    {
        var registry = new RealtimeConnectionRegistry();
        registry.Connect("c", User(Guid.NewGuid()), "bearer");
        Assert.True(registry.TrySubscribe("c", new("crm.account", AccountId), 1));
        Assert.False(registry.TrySubscribe("c", new("crm.account", PricingId), 1));
        Assert.Single(registry.GetSubscribers(new("crm.account", AccountId)));
    }

    [Fact]
    public void Anonymous_identity_cannot_be_connected()
    {
        var registry = new RealtimeConnectionRegistry();
        Assert.Throws<UnauthorizedAccessException>(() =>
            registry.Connect("anon", new ClaimsPrincipal(new ClaimsIdentity()), "token"));
        Assert.Equal(0, registry.ConnectionCount);
    }

    [Fact]
    public async Task Owner_REST_authorization_allows_two_distinct_verified_users()
    {
        var allowed = new HashSet<string> { "user-a", "user-b" };
        var handler = new FakeHandler(request =>
            new HttpResponseMessage(allowed.Contains(request.Headers.Authorization?.Parameter ?? "")
                ? HttpStatusCode.OK : HttpStatusCode.Forbidden));
        var access = Authorizer(handler);
        var userA = User(Guid.NewGuid());
        var userB = User(Guid.NewGuid());
        var resource = new RealtimeResource("crm.account", AccountId);
        Assert.True(await access.CanReadAsync(resource, userA, "user-a", default));
        Assert.True(await access.CanReadAsync(resource, userB, "user-b", default));
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"http://crm-internal:8080/api/crm/accounts/{AccountId:D}",
                request.RequestUri?.ToString());
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(26)]
    [InlineData(27)]
    [InlineData(28)]
    [InlineData(29)]
    public async Task Thirty_forbidden_readers_never_receive_authorization_for_any_resource(int index)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var access = Authorizer(handler);
        var resource = new RealtimeResource("crm.account",
            Guid.Parse($"44444444-4444-4444-8444-{index + 1:D12}"));
        var denied = await access.CanReadAsync(
            resource, User(Guid.NewGuid()), $"denied-user-{index}", default);
        Assert.False(denied);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Owner_API_never_fails_open_on_denied_or_error_status(HttpStatusCode status)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(status));
        Assert.False(await Authorizer(handler).CanReadAsync(
            new("crm.account", AccountId), User(Guid.NewGuid()), "bearer", default));
    }

    [Fact]
    public async Task Missing_bearer_does_not_invoke_owner_service()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var result = await Authorizer(handler).CanReadAsync(
            new("crm.account", AccountId), User(Guid.NewGuid()), null, default);
        Assert.False(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Anonymous_principal_does_not_invoke_owner_service()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        Assert.False(await Authorizer(handler).CanReadAsync(
            new("crm.account", AccountId),
            new ClaimsPrincipal(new ClaimsIdentity()), "bearer", default));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Untrusted_destination_is_rejected_before_network()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        Assert.False(await Authorizer(handler, "http://admin:password@crm-internal:8080")
            .CanReadAsync(new("crm.account", AccountId), User(Guid.NewGuid()), "bearer", default));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task No_trusted_destination_is_rejected_before_network()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var access = Authorizer(handler, "");
        Assert.False(await access.CanReadAsync(new("crm.account", AccountId),
            User(Guid.NewGuid()), "bearer", default));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Hub_refuses_non_guid_user_subjects()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("sub", "email@example.invalid") }, "test-jwt"));
        Assert.False(UniversalRealtimeHub.HasGuidSubject(principal));
    }

    [Fact]
    public void Hub_accepts_verified_guid_subject_claims()
    {
        Assert.True(UniversalRealtimeHub.HasGuidSubject(User(Guid.NewGuid())));
    }
    [Theory]
    [InlineData("https://dhole.customcodecr.com", true)]
    [InlineData("https://sistema.logisticacastrofallas.com", true)]
    [InlineData("http://localhost:5173", true)]
    [InlineData("https://evil.example", false)]
    [InlineData("https://dhole.customcodecr.com.evil.example", false)]
    [InlineData("http://dhole.customcodecr.com", false)]
    [InlineData("https://dhole.customcodecr.com:8443", false)]
    [InlineData("https://user:password@dhole.customcodecr.com", false)]
    [InlineData("https://dhole.customcodecr.com/path", false)]
    [InlineData("https://dhole.customcodecr.com/?x=1", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("null", false)]
    [InlineData("", false)]
    [InlineData("https://*.customcodecr.com", false)]
    [InlineData("ftp://dhole.customcodecr.com", false)]
    public void Websocket_origin_must_match_explicit_CORS_origin(
        string origin, bool allowed)
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["Cors:AllowedOrigins:0"] = "https://dhole.customcodecr.com",
                ["Cors:AllowedOrigins:1"] = "https://sistema.logisticacastrofallas.com",
                ["Cors:AllowedOrigins:2"] = "http://localhost:5173"
            }).Build();
        Assert.Equal(allowed, RealtimeOriginPolicy.IsAllowed(origin, configuration));
    }

}
