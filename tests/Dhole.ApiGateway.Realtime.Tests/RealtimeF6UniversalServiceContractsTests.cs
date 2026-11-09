using System.Text.Json;
using Dhole.ApiGateway.Realtime;
using Xunit;

namespace Dhole.ApiGateway.Realtime.Tests;

public sealed class RealtimeF6UniversalServiceContractsTests
{
    private static string Envelope(Guid eventId, Guid resourceId, string kind,
        string producer, string? eventType = null, string? audienceRef = null)
    {
        var resourceType = kind.Split('.')[1];
        return JsonSerializer.Serialize(new
        {
            eventId,
            eventType = eventType ?? (kind == "audit.event" ? "audit.event.created.v1" :
                kind + ".changed.v1"),
            schemaVersion = 1,
            aggregateType = resourceType,
            aggregateId = resourceId.ToString("D"),
            aggregateVersion = 1,
            occurredAtUtc = DateTimeOffset.UtcNow,
            producer,
            correlationId = "f6-gateway-test",
            audienceRef = audienceRef ?? kind.Replace(".", ":") + ":" + resourceId.ToString("D"),
            payload = new { changedFields = new[] { "resource" } }
        });
    }

    [Theory]
    [InlineData("auth.user", "dhole.auth.realtime.v1", "DholeAuthService")]
    [InlineData("config.catalog-group", "dhole.config.realtime.v1", "DholeConfigService")]
    [InlineData("config.catalog-item", "dhole.config.realtime.v1", "DholeConfigService")]
    [InlineData("srm.supplier", "dhole.config.realtime.v1", "DholeConfigService")]
    [InlineData("audit.event", "dhole.auditlogs.realtime.v1", "DholeAuditLogsService")]
    [InlineData("storage.file", "dhole.storage.realtime.v1", "DholeStorageService")]
    [InlineData("extraction.execution", "dhole.data-extraction.realtime.v1", "DholeDataExtractionService")]
    [InlineData("extraction.emailjob", "dhole.data-extraction.realtime.v1", "DholeDataExtractionService")]
    [InlineData("extraction.airequest", "dhole.data-extraction.realtime.v1", "DholeDataExtractionService")]
    [InlineData("reports.template", "dhole.reports.realtime.v1", "DholeReportsService")]
    [InlineData("dynamics.job", "dhole.dynamics.realtime.v1", "DholeDynamicsConnectorService")]
    [InlineData("ai.execution", "dhole.ai.realtime.v1", "DholeAIService")]
    [InlineData("ai.emailjob", "dhole.ai.realtime.v1", "DholeAIService")]
    [InlineData("agent.execution", "dhole.agent.realtime.v1", "DholeAgentService")]
    [InlineData("agent.definition", "dhole.agent.realtime.v1", "DholeAgentService")]
    [InlineData("agent.schedule", "dhole.agent.realtime.v1", "DholeAgentService")]
    [InlineData("content.item", "dhole.content.realtime.v1", "DholeContentService")]
    [InlineData("content.menu", "dhole.content.realtime.v1", "DholeContentService")]
    [InlineData("content.setting", "dhole.content.realtime.v1", "DholeContentService")]
    public void Each_resource_owner_has_exact_stream_event_and_REST_access_check(
        string kind, string stream, string producer)
    {
        var id = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        Assert.True(RealtimeResourcePolicy.TryCreate(kind, id.ToString("D"), out var allowed));
        Assert.NotNull(RealtimeResourcePolicy.ServiceFor(kind));
        var route = RealtimeResourcePolicy.PathFor(allowed);
        Assert.NotNull(route);
        Assert.Contains(id.ToString("D"), route!, StringComparison.Ordinal);
        Assert.StartsWith("/api/", route!, StringComparison.Ordinal);
        Assert.True(RealtimeStreamBridge.TryParseMinimal(
            Envelope(eventId, id, kind, producer), eventId,
            out var evt, out var resource, stream));
        Assert.Equal(kind, resource.Kind);
        Assert.Equal(id, resource.Id);
        Assert.Equal(eventId, evt.EventId);
        Assert.False(RealtimeStreamBridge.TryParseMinimal(
            Envelope(eventId, id, kind, producer + "_forged"), eventId,
            out _, out _, stream));
        Assert.False(RealtimeStreamBridge.TryParseMinimal(
            Envelope(eventId, id, kind, producer), eventId,
            out _, out _, "other.stream"));
        Assert.False(RealtimeStreamBridge.TryParseMinimal(
            Envelope(eventId, id, kind, producer, kind + ".deleted.v1"), eventId,
            out _, out _, stream));
        Assert.False(RealtimeStreamBridge.TryParseMinimal(
            Envelope(eventId, id, kind, producer,
                audienceRef: "crm:account:" + id.ToString("D")), eventId,
            out _, out _, stream));
    }

    [Fact]
    public void F6_universal_bridge_is_off_without_explicit_approval()
    {
        var flags = new RealtimeHostOptions();
        Assert.False(flags.UniversalServicesBridgeEnabled);
        Assert.False(flags.Enabled);
        Assert.False(flags.BridgeEnabled);
        Assert.False(flags.BatchApproved);
    }

    [Fact]
    public void Unmapped_resources_are_rejected_even_with_valid_guids()
    {
        Assert.False(RealtimeResourcePolicy.TryCreate("content.marketing-lead", Guid.NewGuid().ToString("D"), out _));
        Assert.False(RealtimeResourcePolicy.TryCreate("agent.credential", Guid.NewGuid().ToString("D"), out _));
        Assert.False(RealtimeResourcePolicy.TryCreate("auth.password", Guid.NewGuid().ToString("D"), out _));
        Assert.False(RealtimeResourcePolicy.TryCreate("config.catalog-item", "not-a-guid", out _));
        Assert.False(RealtimeResourcePolicy.TryFromAudienceRef("config:catalog-item:not-a-guid", out _));
        Assert.False(RealtimeResourcePolicy.TryFromAudienceRef("pricing:rate:" +
            Guid.NewGuid().ToString("D") + ":all", out _));
    }
}
