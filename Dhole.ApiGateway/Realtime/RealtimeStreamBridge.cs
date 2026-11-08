using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using StackExchange.Redis;

namespace Dhole.ApiGateway.Realtime;

public sealed record RealtimeInvalidation(
    Guid EventId, string EventType, string AggregateType, Guid AggregateId,
    long AggregateVersion, DateTimeOffset OccurredAtUtc);

/// <summary>
/// F3 gateway-side durable Redis Streams consumer. Each Gateway instance owns a
/// stable, unique consumer GROUP so every instance sees each invalidation; each
/// group ACKs only AFTER local authorized delivery + durable bridge inbox entry.
/// No business mutations are driven from this bridge. Browser reconciles REST.
/// </summary>
public sealed class RealtimeStreamBridge(
    IConnectionMultiplexer redis,
    IHubContext<UniversalRealtimeHub> hub,
    RealtimeConnectionRegistry registry,
    IRealtimeResourceAuthorizer authorizer,
    IOptions<RealtimeHostOptions> options,
    ILogger<RealtimeStreamBridge> logger) : BackgroundService
{
    public const string CrmStream = "dhole.crm.realtime.v1";
    public const string PricingStream = "dhole.pricing.realtime.v1";
    // F6.04-13: strictly isolated opt-in streams per resource owner.
    public static readonly IReadOnlyDictionary<string, string> UniversalServiceStreams =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dhole.auth.realtime.v1"] = "auth",
            ["dhole.config.realtime.v1"] = "config",
            ["dhole.auditlogs.realtime.v1"] = "audit",
            ["dhole.storage.realtime.v1"] = "storage",
            ["dhole.data-extraction.realtime.v1"] = "extraction",
            ["dhole.reports.realtime.v1"] = "reports",
            ["dhole.dynamics.realtime.v1"] = "dynamics",
            ["dhole.ai.realtime.v1"] = "ai",
            ["dhole.agent.realtime.v1"] = "agent",
            ["dhole.content.realtime.v1"] = "content"
        };

    private static readonly Meter Meter = new("Dhole.ApiGateway.Realtime.Bridge", "0.1.0");
    private static readonly Counter<long> Delivered = Meter.CreateCounter<long>("realtime_gateway_delivery");
    private static readonly Counter<long> Denied = Meter.CreateCounter<long>("realtime_gateway_denied");
    private static readonly Counter<long> Failed = Meter.CreateCounter<long>("realtime_gateway_failed");

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;
        if (!opts.Enabled || !opts.BridgeEnabled)
            return;

        var db = redis.GetDatabase();
        // Independent durable cursors and consumer groups per source stream.
        // CRM continues independently if Pricing's opt-in bridge is disabled.
        var bridges = new List<Task> { ConsumeStreamAsync(db, CrmStream, cancellationToken) };
        if (opts.PricingBridgeEnabled)
            bridges.Add(ConsumeStreamAsync(db, PricingStream, cancellationToken));
        if (opts.UniversalServicesBridgeEnabled && opts.BatchApproved &&
            string.Equals(opts.ReleaseId, "rt-20261107-01", StringComparison.Ordinal))
            foreach (var serviceStream in UniversalServiceStreams.Keys)
                bridges.Add(ConsumeStreamAsync(db, serviceStream, cancellationToken));
        await Task.WhenAll(bridges);
    }

    private async Task ConsumeStreamAsync(IDatabase db, string stream,
        CancellationToken ct)
    {
        var opts = options.Value;
        var group = "dhole-rt-gw-v1-" + opts.InstanceId;
        var consumer = opts.InstanceId;
        try
        {
            await db.StreamCreateConsumerGroupAsync(
                stream, group, "0-0", createStream: true);
        }
        catch (RedisServerException ex) when (
            ex.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase))
        {
            // Never reset a persisted cursor on restart.
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var entries = await db.StreamReadGroupAsync(
                    stream, group, consumer, "0-0", count: 50);
                if (entries.Length == 0)
                {
                    var claim = await db.StreamAutoClaimAsync(
                        stream, group, consumer, 30_000, "0-0", count: 50);
                    entries = claim.ClaimedEntries;
                }
                if (entries.Length == 0)
                    entries = await db.StreamReadGroupAsync(
                        stream, group, consumer, ">", count: 50);
                if (entries.Length == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                    continue;
                }
                foreach (var entry in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    await ProcessEntryAsync(db, stream, group, entry, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Failed.Add(1);
                logger.LogWarning("Realtime bridge {Stream} retry after {FailureType}",
                    stream, exception.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task ProcessEntryAsync(IDatabase redisDb, string stream, string group,
        StreamEntry entry, CancellationToken ct)
    {
        var opts = options.Value;
        var fields = entry.Values.ToDictionary(
            x => x.Name.ToString(), x => x.Value.ToString(), StringComparer.Ordinal);
        fields.TryGetValue("payload_json", out var json);
        fields.TryGetValue("event_id", out var idText);
        // RedisStreamPublisher may expose event_id either as a top-level field
        // or via headers. Do not trust either without matching the JSON eventId.
        if (string.IsNullOrWhiteSpace(idText))
            fields.TryGetValue("header:event_id", out idText);
        if (Encoding.UTF8.GetByteCount(json ?? "") > 6000 ||
            !Guid.TryParse(idText, out var eventId) || eventId == Guid.Empty ||
            !TryParseMinimal(json, eventId, out var invalidation, out var resource, stream))
        {
            await QuarantineAsync(opts.InboxPostgresConnectionString, opts.InstanceId,
                stream, entry.Id.ToString(), "invalid_or_unmapped_envelope", ct);
            await redisDb.StreamAcknowledgeAsync(stream, group, entry.Id);
            return;
        }
        if (await AlreadyProcessedAsync(opts.InboxPostgresConnectionString, opts.InstanceId,
                eventId, ct))
        {
            await redisDb.StreamAcknowledgeAsync(stream, group, entry.Id);
            return;
        }

        foreach (var subscriber in registry.GetSubscribers(resource))
        {
            // Check permissions for EVERY message, not only when joining a group.
            var permitted = await authorizer.CanReadAsync(
                resource, subscriber.User, subscriber.DelegatedBearer, ct);
            if (!permitted)
            {
                registry.Unsubscribe(subscriber.ConnectionId, resource);
                await hub.Groups.RemoveFromGroupAsync(
                    subscriber.ConnectionId, resource.GroupName, ct);
                // F6: immediately instruct this previously subscribed client to
                // purge its cached REST snapshot. No business event or customer
                // data is disclosed after revocation; payload is only kind + ID
                // already known to the subscriber.
                await hub.Clients.Client(subscriber.ConnectionId)
                    .SendCoreAsync("resourceRevoked",
                        new object?[] { resource.Kind, resource.Id.ToString("D") }, ct);
                Denied.Add(1);
                continue;
            }

            // Target a local authenticated connection, never Clients.All,
            // Clients.Group or the untrusted audienceRef.
            await hub.Clients.Client(subscriber.ConnectionId)
                .SendCoreAsync("resourceChanged", new object?[] { invalidation }, ct);
            Delivered.Add(1);
        }

        // On a bridge crash before the commit/ACK, events can repeat. UI must
        // dedupe eventId and always reconcile a version gap with REST.
        await RecordProcessedAsync(opts.InboxPostgresConnectionString,
            opts.InstanceId, eventId, ct);
        await redisDb.StreamAcknowledgeAsync(stream, group, entry.Id);
    }

    public static bool TryParseMinimal(
        string? json, Guid expectedEventId,
        out RealtimeInvalidation invalidation, out RealtimeResource resource,
        string stream = CrmStream)
    {
        invalidation = default!;
        resource = default;
        if (string.IsNullOrWhiteSpace(json) || json.Length > 6000 ||
            expectedEventId == Guid.Empty) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("eventId", out var eventField) ||
                !Guid.TryParse(eventField.GetString(), out var id) || id != expectedEventId ||
                !root.TryGetProperty("eventType", out var eventType) ||
                eventType.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("schemaVersion", out var schema) ||
                schema.GetInt32() <= 0 ||
                !root.TryGetProperty("aggregateType", out var aggregateType) ||
                aggregateType.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("aggregateId", out var aggregateId) ||
                aggregateId.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(aggregateId.GetString(), out var aggregateGuid) ||
                aggregateGuid == Guid.Empty ||
                !root.TryGetProperty("aggregateVersion", out var version) ||
                version.GetInt64() <= 0 ||
                !root.TryGetProperty("occurredAtUtc", out var time) ||
                !DateTimeOffset.TryParse(time.GetString(), out var occurred) ||
                occurred.Offset != TimeSpan.Zero ||
                !root.TryGetProperty("audienceRef", out var audience) ||
                !RealtimeResourcePolicy.TryFromAudienceRef(audience.GetString(), out resource))
                return false;
            var type = eventType.GetString();
            if (string.IsNullOrWhiteSpace(type) || type.Length > 160 ||
                !type.EndsWith(".v1", StringComparison.Ordinal))
                return false;
            // Every dedicated stream has an exact resource/producer/event kind.
            // No arbitrary consumer-selected group, audience or event names.
            var correctProducer = stream switch
            {
                CrmStream => resource.Kind == "crm.account" &&
                    aggregateType.GetString() == "account" &&
                    type.StartsWith("crm.account.", StringComparison.Ordinal),
                PricingStream => resource.Kind == "pricing.rate" &&
                    aggregateType.GetString() == "rate" &&
                    type.StartsWith("pricing.rate.", StringComparison.Ordinal),
                _ => MatchesUniversalProducer(stream, resource, type,
                    aggregateType.GetString(),
                    root.TryGetProperty("producer", out var producer) &&
                    producer.ValueKind == JsonValueKind.String ? producer.GetString() : null)
            };
            if (!correctProducer) return false;
            if (aggregateGuid != resource.Id) return false;
            invalidation = new(
                id, type, aggregateType.GetString()!, aggregateGuid,
                version.GetInt64(), occurred);
            return true;
        }
        catch (Exception exception) when (
            exception is JsonException or FormatException or InvalidOperationException)
        {
            return false;
        }
    }

    public static bool MatchesUniversalProducer(string stream, RealtimeResource resource,
        string type, string? aggregateType, string? producer)
    {
        if (!UniversalServiceStreams.TryGetValue(stream, out var domain))
            return false;
        var exactKind = resource.Kind.StartsWith(domain + ".", StringComparison.Ordinal) &&
            aggregateType == resource.Kind[(domain.Length + 1)..];
        var srmCatalogAlias = domain == "config" &&
            resource.Kind == "srm.supplier" && aggregateType == "supplier";
        if (!exactKind && !srmCatalogAlias)
            return false;
        var expectedProducer = domain switch
        {
            "auth" => "DholeAuthService",
            "config" => "DholeConfigService",
            "audit" => "DholeAuditLogsService",
            "storage" => "DholeStorageService",
            "extraction" => "DholeDataExtractionService",
            "reports" => "DholeReportsService",
            "dynamics" => "DholeDynamicsConnectorService",
            "ai" => "DholeAIService",
            "agent" => "DholeAgentService",
            "content" => "DholeContentService",
            _ => null
        };
        var expectedType = resource.Kind == "audit.event"
            ? "audit.event.created.v1"
            : resource.Kind + ".changed.v1";
        return string.Equals(producer, expectedProducer, StringComparison.Ordinal) &&
            string.Equals(type, expectedType, StringComparison.Ordinal);
    }

    private static async Task<bool> AlreadyProcessedAsync(
        string connectionString, string instance, Guid id, CancellationToken ct)
    {
        await using var db = new NpgsqlConnection(connectionString);
        await db.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
SELECT EXISTS (
    SELECT 1 FROM realtime.gateway_inbox
    WHERE event_id = @id AND instance_id = @instance
);
""", db);
        cmd.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });
        cmd.Parameters.Add(new NpgsqlParameter("instance", NpgsqlDbType.Varchar) { Value = instance });
        return (bool)(await cmd.ExecuteScalarAsync(ct) ?? false);
    }

    private static async Task RecordProcessedAsync(
        string connectionString, string instance, Guid id, CancellationToken ct)
    {
        await using var db = new NpgsqlConnection(connectionString);
        await db.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
INSERT INTO realtime.gateway_inbox (event_id, instance_id, processed_at)
VALUES (@id, @instance, now())
ON CONFLICT (event_id, instance_id) DO NOTHING;
""", db);
        cmd.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });
        cmd.Parameters.Add(new NpgsqlParameter("instance", NpgsqlDbType.Varchar) { Value = instance });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task QuarantineAsync(
        string connectionString, string instance, string stream,
        string streamId, string reason, CancellationToken ct)
    {
        await using var db = new NpgsqlConnection(connectionString);
        await db.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
INSERT INTO realtime.gateway_deadletter
    (instance_id, stream_name, stream_id, reason)
VALUES (@instance, @stream, @stream_id, @reason)
ON CONFLICT (instance_id, stream_name, stream_id) DO NOTHING;
""", db);
        cmd.Parameters.AddWithValue("instance", instance);
        cmd.Parameters.AddWithValue("stream", stream);
        cmd.Parameters.AddWithValue("stream_id", streamId);
        cmd.Parameters.AddWithValue("reason", reason);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
