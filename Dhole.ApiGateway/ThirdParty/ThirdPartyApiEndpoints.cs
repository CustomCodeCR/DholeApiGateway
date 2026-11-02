using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dhole.ApiGateway.Gateway;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Dhole.ApiGateway.ThirdParty;

// A published contract never contains credentials or a free-form upstream URL.
// Targets are explicitly approved in ThirdParty:Targets configuration.
public sealed record PublishedField(string Public, string Internal, bool Required = false);
public sealed record PublishedEndpoint(
    Guid Id, string Name, string Slug, string Target,
    List<PublishedField> Inputs, List<PublishedField> Outputs,
    string? ResponseRoot, bool Enabled);
public sealed record ApprovedTarget(string Code, string Service, string Method, string Path, string Description);
public sealed record IssueKeyRequest(string Label, int ExpiresInDays = 90, int MaxRequestsPerMinute = 30);

public static class ThirdPartyApiEndpoints
{
    private const string Manage = "integrations.thirdparty.manage";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex Slug = new("^[a-z][a-z0-9-]{2,63}$", RegexOptions.Compiled);
    private static readonly Regex Field = new("^[a-zA-Z][a-zA-Z0-9_]{0,79}$", RegexOptions.Compiled);
    private static readonly Regex Source = new("^[a-zA-Z][a-zA-Z0-9_.]{0,159}$", RegexOptions.Compiled);
    private static readonly Regex Identifier = new("^[a-zA-Z0-9_.-]{1,128}$", RegexOptions.Compiled);
    private static readonly string[] SupportedMethods = ["GET", "POST", "PUT", "DELETE"];

    public static async Task EnsureSchemaAsync(IConfiguration cfg, CancellationToken ct = default)
    {
        await using var db = await Open(cfg, ct);
        const string sql = """
            create schema if not exists integrations;
            create table if not exists integrations.public_endpoints (
                id uuid primary key, slug text not null unique, document jsonb not null,
                enabled boolean not null default false, updated_at timestamptz not null default now()
            );
            create table if not exists integrations.public_keys (
                id uuid primary key, endpoint_id uuid not null references integrations.public_endpoints(id),
                label text not null, prefix text not null, secret_hash text not null unique,
                active boolean not null default true, expires_at timestamptz not null,
                max_per_minute integer not null, created_at timestamptz not null default now(),
                last_used_at timestamptz
            );
            create table if not exists integrations.public_usage (
                key_id uuid not null references integrations.public_keys(id),
                minute timestamptz not null, count integer not null,
                primary key (key_id, minute)
            );
            create table if not exists integrations.public_audit (
                id bigint generated always as identity primary key,
                endpoint_id uuid not null, key_id uuid not null, method text not null,
                http_status integer not null, duration_ms bigint not null,
                created_at timestamptz not null default now()
            );
            create index if not exists idx_public_audit_endpoint on integrations.public_audit(endpoint_id, created_at desc);
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static void MapThirdPartyEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/integrations/third-party").RequireAuthorization();

        admin.MapGet("/catalog", (HttpContext ctx, IConfiguration cfg) =>
            Authorized(ctx) ? Results.Ok(Targets(cfg)) : Results.Forbid());

        admin.MapGet("/endpoints", async (HttpContext ctx, IConfiguration cfg) =>
        {
            if (!Authorized(ctx)) return Results.Forbid();
            await using var db = await Open(cfg, ctx.RequestAborted);
            await using var cmd = new NpgsqlCommand(
                "select document::text from integrations.public_endpoints order by slug", db);
            await using var reader = await cmd.ExecuteReaderAsync(ctx.RequestAborted);
            var definitions = new List<PublishedEndpoint>();
            while (await reader.ReadAsync(ctx.RequestAborted))
                definitions.Add(JsonSerializer.Deserialize<PublishedEndpoint>(reader.GetString(0), Json)!);
            return Results.Ok(definitions);
        });

        admin.MapPost("/endpoints", async (HttpContext ctx, IConfiguration cfg, PublishedEndpoint input) =>
        {
            if (!Authorized(ctx)) return Results.Forbid();
            var definition = input with { Id = Guid.NewGuid(), Enabled = false };
            var error = Validate(definition, cfg);
            if (error is not null) return Results.BadRequest(new { message = error });
            await using var db = await Open(cfg, ctx.RequestAborted);
            await using var cmd = new NpgsqlCommand(
                "insert into integrations.public_endpoints(id,slug,document,enabled) values (@id,@slug,@doc,false)", db);
            AddDefinitionParams(cmd, definition);
            await cmd.ExecuteNonQueryAsync(ctx.RequestAborted);
            return Results.Created("/api/integrations/third-party/endpoints/" + definition.Id, definition);
        });

        admin.MapPut("/endpoints/{id:guid}", async (HttpContext ctx, IConfiguration cfg, Guid id, PublishedEndpoint input) =>
        {
            if (!Authorized(ctx)) return Results.Forbid();
            var definition = input with { Id = id };
            var error = Validate(definition, cfg);
            if (error is not null) return Results.BadRequest(new { message = error });
            await using var db = await Open(cfg, ctx.RequestAborted);
            // Disabling a definition always revokes runtime access to its existing keys.
            await using var cmd = new NpgsqlCommand(
                "update integrations.public_endpoints set slug=@slug,document=@doc,enabled=@enabled,updated_at=now() where id=@id", db);
            AddDefinitionParams(cmd, definition);
            cmd.Parameters.AddWithValue("enabled", definition.Enabled);
            return await cmd.ExecuteNonQueryAsync(ctx.RequestAborted) == 0
                ? Results.NotFound() : Results.Ok(definition);
        });

        admin.MapGet("/endpoints/{id:guid}/keys", async (HttpContext ctx, IConfiguration cfg, Guid id) =>
        {
            if (!Authorized(ctx)) return Results.Forbid();
            await using var db = await Open(cfg, ctx.RequestAborted);
            await using var cmd = new NpgsqlCommand(
                "select id,label,prefix,active,expires_at,max_per_minute,created_at,last_used_at from integrations.public_keys where endpoint_id=@id order by created_at desc", db);
            cmd.Parameters.AddWithValue("id", id);
            await using var r = await cmd.ExecuteReaderAsync(ctx.RequestAborted);
            var keys = new List<object>();
            while (await r.ReadAsync(ctx.RequestAborted))
                keys.Add(new {
                    id = r.GetGuid(0), label = r.GetString(1), prefix = r.GetString(2),
                    active = r.GetBoolean(3), expiresAt = r.GetDateTime(4), maxRequestsPerMinute = r.GetInt32(5),
                    createdAt = r.GetDateTime(6), lastUsedAt = r.IsDBNull(7) ? (DateTime?)null : r.GetDateTime(7)
                });
            return Results.Ok(keys);
        });

        admin.MapPost("/endpoints/{id:guid}/keys", async (HttpContext ctx, IConfiguration cfg, Guid id, IssueKeyRequest request) =>
        {
            if (!Authorized(ctx)) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(request.Label) || request.Label.Length > 100 ||
                request.ExpiresInDays is < 1 or > 365 || request.MaxRequestsPerMinute is < 1 or > 300)
                return Results.BadRequest(new { message = "Nombre, vencimiento o límite inválido." });
            await using var db = await Open(cfg, ctx.RequestAborted);
            await using (var check = new NpgsqlCommand("select enabled from integrations.public_endpoints where id=@id", db))
            {
                check.Parameters.AddWithValue("id", id);
                if (await check.ExecuteScalarAsync(ctx.RequestAborted) is not true)
                    return Results.BadRequest(new { message = "Publicá el endpoint antes de generar una clave." });
            }
            var secret = "dh_ext_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var hash = Sha(secret);
            var keyId = Guid.NewGuid();
            await using var cmd = new NpgsqlCommand(
                "insert into integrations.public_keys(id,endpoint_id,label,prefix,secret_hash,expires_at,max_per_minute) values(@id,@ep,@label,@prefix,@hash,@expires,@limit)", db);
            cmd.Parameters.AddWithValue("id", keyId);
            cmd.Parameters.AddWithValue("ep", id);
            cmd.Parameters.AddWithValue("label", request.Label.Trim());
            cmd.Parameters.AddWithValue("prefix", secret[..15]);
            cmd.Parameters.AddWithValue("hash", hash);
            cmd.Parameters.AddWithValue("expires", DateTime.UtcNow.AddDays(request.ExpiresInDays));
            cmd.Parameters.AddWithValue("limit", request.MaxRequestsPerMinute);
            await cmd.ExecuteNonQueryAsync(ctx.RequestAborted);
            return Results.Ok(new { id = keyId, apiKey = secret }); // Display only once.
        });

        admin.MapDelete("/keys/{id:guid}", async (HttpContext ctx, IConfiguration cfg, Guid id) =>
        {
            if (!Authorized(ctx)) return Results.Forbid();
            await using var db = await Open(cfg, ctx.RequestAborted);
            await using var cmd = new NpgsqlCommand("update integrations.public_keys set active=false where id=@id", db);
            cmd.Parameters.AddWithValue("id", id);
            return await cmd.ExecuteNonQueryAsync(ctx.RequestAborted) == 0 ? Results.NotFound() : Results.NoContent();
        });

        // Third parties authenticate exclusively with X-Api-Key, never with Dhole user JWT.
        app.MapMethods("/api/external/{slug}/{id?}", SupportedMethods,
            async (HttpContext ctx, IConfiguration cfg, IOptions<GatewayOptions> gateway,
                IHttpClientFactory clients, string slug, string? id) =>
            {
                var ct = ctx.RequestAborted;
                if (id is not null && !Identifier.IsMatch(id)) return Results.BadRequest();
                if (!ctx.Request.Headers.TryGetValue("X-Api-Key", out var provided)
                    || provided.Count != 1 || provided[0] is not { Length: > 20 } rawKey
                    || !rawKey.StartsWith("dh_ext_", StringComparison.Ordinal))
                    return Results.Unauthorized();
                await using var db = await Open(cfg, ct);
                await using var definitionCmd = new NpgsqlCommand(
                    "select id,document::text from integrations.public_endpoints where slug=@slug and enabled=true", db);
                definitionCmd.Parameters.AddWithValue("slug", slug);
                Guid definitionId;
                PublishedEndpoint definition;
                await using (var reader = await definitionCmd.ExecuteReaderAsync(ct))
                {
                    if (!await reader.ReadAsync(ct)) return Results.NotFound();
                    definitionId = reader.GetGuid(0);
                    definition = JsonSerializer.Deserialize<PublishedEndpoint>(reader.GetString(1), Json)!;
                }
                var target = Targets(cfg).FirstOrDefault(t => t.Code == definition.Target);
                if (target is null || !string.Equals(ctx.Request.Method, target.Method, StringComparison.OrdinalIgnoreCase))
                    return Results.NotFound();
                if (target.Path.Contains("{id}", StringComparison.Ordinal) != (id is not null))
                    return Results.BadRequest(new { message = "Identificador no válido para esta operación." });
                if (target.Path.Contains("{id}", StringComparison.Ordinal) && id is null)
                    return Results.BadRequest(new { message = "Se requiere un identificador." });

                await using var keyCmd = new NpgsqlCommand(
                    "select id,max_per_minute from integrations.public_keys where endpoint_id=@ep and secret_hash=@hash and active=true and expires_at>now()", db);
                keyCmd.Parameters.AddWithValue("ep", definitionId);
                keyCmd.Parameters.AddWithValue("hash", Sha(rawKey));
                Guid keyId;
                int quota;
                await using (var reader = await keyCmd.ExecuteReaderAsync(ct))
                {
                    if (!await reader.ReadAsync(ct)) return Results.Unauthorized();
                    keyId = reader.GetGuid(0);
                    quota = reader.GetInt32(1);
                }

                await using (var limiter = new NpgsqlCommand(
                    """
                    insert into integrations.public_usage(key_id, minute, count)
                    values (@key, date_trunc('minute', now()), 1)
                    on conflict(key_id,minute) do update set count=integrations.public_usage.count+1
                    where integrations.public_usage.count < @quota
                    returning count
                    """, db))
                {
                    limiter.Parameters.AddWithValue("key", keyId);
                    limiter.Parameters.AddWithValue("quota", quota);
                    if (await limiter.ExecuteScalarAsync(ct) is null)
                        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
                }

                // Only destinations already registered by Dhole Gateway are routable.
                var prefix = "/api/" + target.Service;
                var route = gateway.Value.Routes.FirstOrDefault(r =>
                    r.Prefix.Equals(prefix, StringComparison.OrdinalIgnoreCase));
                var serviceCredential = cfg["ThirdParty:ServiceTokens:" + target.Code];
                if (route is null || string.IsNullOrWhiteSpace(serviceCredential))
                    return Results.Problem("La identidad interna de integración no está configurada.", statusCode: 503);
                var suffix = target.Path.Replace("{id}", Uri.EscapeDataString(id ?? ""), StringComparison.Ordinal);
                if (route.StripPrefix) suffix = suffix[prefix.Length..];
                var destination = route.Destination.TrimEnd('/') + suffix;
                if (!Uri.TryCreate(destination, UriKind.Absolute, out var uri) ||
                    uri.Scheme is not ("http" or "https"))
                    return Results.Problem(statusCode: 503);

                var outgoing = new HttpRequestMessage(new HttpMethod(target.Method), uri);
                outgoing.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", serviceCredential);
                outgoing.Headers.TryAddWithoutValidation("X-Correlation-Id", ctx.TraceIdentifier);

                if (target.Method == "GET")
                {
                    var query = new List<string>();
                    foreach (var field in definition.Inputs)
                    {
                        var value = ctx.Request.Query[field.Public].ToString();
                        if (field.Required && string.IsNullOrWhiteSpace(value))
                            return Results.BadRequest(new { message = "Falta el parámetro " + field.Public });
                        if (value.Length > 2048) return Results.BadRequest();
                        if (value.Length > 0) query.Add(Uri.EscapeDataString(field.Internal) + "=" + Uri.EscapeDataString(value));
                    }
                    if (ctx.Request.Query.Keys.Any(k => !definition.Inputs.Any(f => f.Public == k)))
                        return Results.BadRequest(new { message = "Parámetro no publicado." });
                    if (query.Count > 0)
                        outgoing.RequestUri = new Uri(destination + "?" + string.Join("&", query));
                }
                else if (definition.Inputs.Count > 0)
                {
                    JsonObject? input;
                    try { input = await ctx.Request.ReadFromJsonAsync<JsonObject>(cancellationToken: ct); }
                    catch (Exception ex) when (ex is JsonException or BadHttpRequestException or InvalidOperationException)
                    { return Results.BadRequest(new { message = "JSON inválido." }); }
                    if (input is null || input.Any(kv => !definition.Inputs.Any(f => f.Public == kv.Key)))
                        return Results.BadRequest(new { message = "El cuerpo tiene campos no publicados." });
                    var body = new JsonObject();
                    foreach (var field in definition.Inputs)
                    {
                        if (field.Required && (!input.ContainsKey(field.Public) || input[field.Public] is null))
                            return Results.BadRequest(new { message = "Falta " + field.Public });
                        if (input.TryGetPropertyValue(field.Public, out var value) && value is not null)
                            SetNode(body, field.Internal, value.DeepClone());
                    }
                    outgoing.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                }
                else if (ctx.Request.ContentLength is > 0)
                    return Results.BadRequest(new { message = "Este endpoint no recibe cuerpo JSON." });

                var started = System.Diagnostics.Stopwatch.StartNew();
                int status = 502;
                try
                {
                    using (outgoing)
                    using (var response = await clients.CreateClient("third-party-upstream").SendAsync(outgoing, ct))
                    {
                        status = (int)response.StatusCode;
                        if (response.StatusCode == HttpStatusCode.NoContent)
                            return Results.NoContent();
                        var raw = await response.Content.ReadAsStringAsync(ct);
                        if (!response.IsSuccessStatusCode)
                            return Results.Json(new { code = "External.UpstreamRejected", status, traceId = ctx.TraceIdentifier },
                                statusCode: status >= 400 && status <= 599 ? status : 502);
                        JsonNode? parsed;
                        try { parsed = JsonNode.Parse(raw); }
                        catch (JsonException) { return Results.Problem("Respuesta no JSON del servicio interno.", statusCode: 502); }
                        var data = GetNode(parsed, definition.ResponseRoot);
                        JsonNode projected = data is JsonArray array
                            ? new JsonArray(array.Select(row => (JsonNode?)Project(row, definition.Outputs)).ToArray())
                            : Project(data, definition.Outputs);
                        return Results.Json(projected, statusCode: status);
                    }
                }
                catch (HttpRequestException)
                {
                    return Results.Problem("El servicio interno no está disponible.", statusCode: 502);
                }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested)
                {
                    return Results.Problem("Tiempo de respuesta agotado.", statusCode: 504);
                }
                finally
                {
                    try
                    {
                        await using var audit = new NpgsqlCommand(
                            "insert into integrations.public_audit(endpoint_id,key_id,method,http_status,duration_ms) values(@e,@k,@m,@s,@d)", db);
                        audit.Parameters.AddWithValue("e", definitionId);
                        audit.Parameters.AddWithValue("k", keyId);
                        audit.Parameters.AddWithValue("m", target.Method);
                        audit.Parameters.AddWithValue("s", status);
                        audit.Parameters.AddWithValue("d", started.ElapsedMilliseconds);
                        await audit.ExecuteNonQueryAsync(CancellationToken.None);
                        await using var used = new NpgsqlCommand(
                            "update integrations.public_keys set last_used_at=now() where id=@k", db);
                        used.Parameters.AddWithValue("k", keyId);
                        await used.ExecuteNonQueryAsync(CancellationToken.None);
                    }
                    catch (NpgsqlException) { /* Integration response is not replaced by audit failures. */ }
                }
            }).AllowAnonymous();
    }

    private static JsonNode? GetNode(JsonNode? node, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return node;
        foreach (var segment in path.Split('.'))
            node = node is JsonObject obj ? obj[segment] : null;
        return node;
    }

    private static JsonObject Project(JsonNode? source, IEnumerable<PublishedField> fields)
    {
        var result = new JsonObject();
        foreach (var field in fields)
        {
            var node = GetNode(source, field.Internal);
            if (node is not null) result[field.Public] = node.DeepClone();
        }
        return result;
    }

    private static void SetNode(JsonObject target, string path, JsonNode value)
    {
        var segments = path.Split('.');
        var current = target;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (current[segments[i]] is not JsonObject) current[segments[i]] = new JsonObject();
            current = (JsonObject)current[segments[i]]!;
        }
        current[segments[^1]] = value;
    }

    private static string? Validate(PublishedEndpoint d, IConfiguration cfg)
    {
        if (!Slug.IsMatch(d.Slug) || string.IsNullOrWhiteSpace(d.Name) || d.Name.Length > 100)
            return "Nombre o identificador de API inválido.";
        var target = Targets(cfg).FirstOrDefault(t => t.Code == d.Target);
        if (target is null) return "Seleccioná un endpoint interno aprobado.";
        if (d.Outputs.Count is < 1 or > 64 || d.Inputs.Count > 64)
            return "Elegí entre 1 y 64 campos de respuesta y máximo 64 entradas.";
        if (d.Outputs.Concat(d.Inputs).Any(f => !Field.IsMatch(f.Public) || !Source.IsMatch(f.Internal)))
            return "Los nombres de los campos deben ser identificadores válidos.";
        if (d.Inputs.Select(f => f.Public).Distinct(StringComparer.Ordinal).Count() != d.Inputs.Count ||
            d.Outputs.Select(f => f.Public).Distinct(StringComparer.Ordinal).Count() != d.Outputs.Count)
            return "Los campos públicos no pueden repetirse.";
        if (!string.IsNullOrEmpty(d.ResponseRoot) && !Source.IsMatch(d.ResponseRoot))
            return "Ruta de datos de respuesta inválida.";
        return null;
    }

    private static List<ApprovedTarget> Targets(IConfiguration cfg)
    {
        return (cfg.GetSection("ThirdParty:Targets").Get<List<ApprovedTarget>>() ?? [])
            .Where(t => SupportedMethods.Contains(t.Method) &&
                        t.Path.StartsWith("/api/" + t.Service + "/", StringComparison.OrdinalIgnoreCase) &&
                        !t.Path.Contains('?') &&
                        !t.Path.Contains('#') &&
                        !t.Path.Contains("..", StringComparison.Ordinal) &&
                        !t.Path.Contains("//", StringComparison.Ordinal) &&
                        Regex.IsMatch(t.Path, "^/api/[a-z-]+/[a-zA-Z0-9_/{}/.-]+$") &&
                        (t.Path.IndexOf('{') < 0 ||
                         t.Path.EndsWith("/{id}", StringComparison.Ordinal)))
            .ToList();
    }

    private static void AddDefinitionParams(NpgsqlCommand cmd, PublishedEndpoint definition)
    {
        cmd.Parameters.AddWithValue("id", definition.Id);
        cmd.Parameters.AddWithValue("slug", definition.Slug);
        cmd.Parameters.Add(new NpgsqlParameter("doc", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(definition, Json) });
    }

    private static bool Authorized(HttpContext ctx)
    {
        if (ctx.User.Identity?.IsAuthenticated != true) return false;
        return ctx.User.Claims.Where(c => c.Type is "scope" or "scp" or "scopes")
            .SelectMany(c => c.Value.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries))
            .Contains(Manage, StringComparer.OrdinalIgnoreCase);
    }

    private static string Sha(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static async Task<NpgsqlConnection> Open(IConfiguration cfg, CancellationToken ct)
    {
        var connectionString = cfg["ThirdParty:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ThirdParty:ConnectionString no configurada.");
        var db = new NpgsqlConnection(connectionString);
        await db.OpenAsync(ct);
        return db;
    }
}
