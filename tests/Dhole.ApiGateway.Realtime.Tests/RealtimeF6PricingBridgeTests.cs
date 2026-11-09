using System.Text.Json;
using Dhole.ApiGateway.Realtime;
using Xunit;

namespace Dhole.ApiGateway.Realtime.Tests;

/// <summary>
/// F6.02: the existing CRM Bridge must reject forged Pricing messages,
/// while the Pricing stream delivers only its own rate resource references.
/// </summary>
public sealed class RealtimeF6PricingBridgeTests
{
    private static string Envelope(Guid eventId, Guid rateId, string action = "updated",
        string audiencePrefix = "pricing:rate:", long version = 2) =>
        JsonSerializer.Serialize(new
        {
            eventId,
            eventType = "pricing.rate." + action + ".v1",
            schemaVersion = 1,
            aggregateType = "rate",
            aggregateId = rateId.ToString("D"),
            aggregateVersion = version,
            occurredAtUtc = DateTimeOffset.UtcNow,
            producer = "DholePricingService",
            correlationId = "f6-contract-test",
            audienceRef = audiencePrefix + rateId.ToString("D"),
            payload = new { changedFields = new[] { "rate" } }
        });

    [Theory]
    [InlineData("created")]
    [InlineData("updated")]
    [InlineData("deleted")]
    public void Dedicated_Pricing_stream_accepts_its_own_minimal_envelopes(string action)
    {
        var eventId = Guid.NewGuid();
        var rateId = Guid.NewGuid();
        Assert.True(RealtimeStreamBridge.TryParseMinimal(
            Envelope(eventId, rateId, action), eventId,
            out var invalidation, out var resource, RealtimeStreamBridge.PricingStream));
        Assert.Equal(rateId, resource.Id);
        Assert.Equal("pricing.rate", resource.Kind);
        Assert.Equal("rate", invalidation.AggregateType);
        Assert.Equal("pricing.rate." + action + ".v1", invalidation.EventType);
        Assert.Equal(2, invalidation.AggregateVersion);
    }

    [Fact]
    public void CRM_stream_refuses_Pricing_and_Pricing_stream_refuses_CRM()
    {
        var eventId = Guid.NewGuid();
        var id = Guid.NewGuid();
        Assert.False(RealtimeStreamBridge.TryParseMinimal(
            Envelope(eventId, id), eventId, out _, out _, RealtimeStreamBridge.CrmStream));
        var crm = JsonSerializer.Serialize(new
        {
            eventId,
            eventType = "crm.account.updated.v1",
            schemaVersion = 1,
            aggregateType = "account",
            aggregateId = id.ToString("D"),
            aggregateVersion = 3,
            occurredAtUtc = DateTimeOffset.UtcNow,
            producer = "DholeCRMService",
            correlationId = "f6-ci",
            audienceRef = $"crm:account:{id:D}",
            payload = new { changedFields = new[] { "account" } }
        });
        Assert.False(RealtimeStreamBridge.TryParseMinimal(
            crm, eventId, out _, out _, RealtimeStreamBridge.PricingStream));
    }

    [Fact]
    public void Pricing_stream_never_accepts_forged_audience_unknown_resource_or_stale_version()
    {
        var eventId = Guid.NewGuid();
        var rateId = Guid.NewGuid();
        Assert.False(RealtimeStreamBridge.TryParseMinimal(
            Envelope(eventId, rateId, audiencePrefix: "crm:account:"),
            eventId, out _, out _, RealtimeStreamBridge.PricingStream));
        Assert.False(RealtimeStreamBridge.TryParseMinimal(
            Envelope(eventId, rateId, version: 0),
            eventId, out _, out _, RealtimeStreamBridge.PricingStream));
        Assert.False(RealtimeStreamBridge.TryParseMinimal(
            Envelope(eventId, rateId), Guid.NewGuid(), out _, out _,
            RealtimeStreamBridge.PricingStream));
        Assert.False(RealtimeStreamBridge.TryParseMinimal(
            Envelope(eventId, rateId), eventId, out _, out _, "untrusted-stream"));
    }

    [Fact]
    public void Pricing_bridge_is_off_by_default()
    {
        var o = new RealtimeHostOptions();
        Assert.False(o.PricingBridgeEnabled);
        Assert.False(o.BridgeEnabled);
        Assert.False(o.Enabled);
    }
}
