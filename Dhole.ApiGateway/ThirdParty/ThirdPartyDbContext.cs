using Microsoft.EntityFrameworkCore;

namespace Dhole.ApiGateway.ThirdParty;

// EF Core owns the integration schema and its migrations. Runtime endpoint queries
// may continue using Npgsql directly; both use the same database/contract.
public sealed class ThirdPartyDbContext(DbContextOptions<ThirdPartyDbContext> options) : DbContext(options)
{
    public DbSet<ThirdPartyEndpointRow> Endpoints => Set<ThirdPartyEndpointRow>();
    public DbSet<ThirdPartyKeyRow> Keys => Set<ThirdPartyKeyRow>();
    public DbSet<ThirdPartyUsageRow> Usage => Set<ThirdPartyUsageRow>();
    public DbSet<ThirdPartyAuditRow> Audit => Set<ThirdPartyAuditRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => ThirdPartyInitialModel.Build(modelBuilder);
}

public sealed class ThirdPartyEndpointRow
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = "";
    public string Document { get; set; } = "{}";
    public bool Enabled { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ThirdPartyKeyRow
{
    public Guid Id { get; set; }
    public Guid EndpointId { get; set; }
    public string Label { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string SecretHash { get; set; } = "";
    public bool Active { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int MaxPerMinute { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public ThirdPartyEndpointRow Endpoint { get; set; } = null!;
}

public sealed class ThirdPartyUsageRow
{
    public Guid KeyId { get; set; }
    public DateTime Minute { get; set; }
    public int Count { get; set; }
    public ThirdPartyKeyRow Key { get; set; } = null!;
}

public sealed class ThirdPartyAuditRow
{
    public long Id { get; set; }
    public Guid EndpointId { get; set; }
    public Guid KeyId { get; set; }
    public string Method { get; set; } = "";
    public int HttpStatus { get; set; }
    public long DurationMs { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Frozen initial model for migration target and snapshot. When the schema changes,
// scaffold a NEW migration and update the snapshot; do not edit the initial migration.
internal static class ThirdPartyInitialModel
{
    public static void Build(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("integrations");

        modelBuilder.Entity<ThirdPartyEndpointRow>(b =>
        {
            b.ToTable("public_endpoints");
            b.HasKey(x => x.Id).HasName("public_endpoints_pkey");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.Slug).HasColumnName("slug").HasColumnType("text").IsRequired();
            b.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("ix_public_endpoints_slug");
            b.Property(x => x.Document).HasColumnName("document").HasColumnType("jsonb").IsRequired();
            b.Property(x => x.Enabled).HasColumnName("enabled").HasDefaultValue(false);
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at")
                .HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<ThirdPartyKeyRow>(b =>
        {
            b.ToTable("public_keys");
            b.HasKey(x => x.Id).HasName("public_keys_pkey");
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.EndpointId).HasColumnName("endpoint_id");
            b.HasOne(x => x.Endpoint).WithMany().HasForeignKey(x => x.EndpointId)
                .HasConstraintName("public_keys_endpoint_id_fkey").OnDelete(DeleteBehavior.NoAction);
            b.Property(x => x.Label).HasColumnName("label").HasColumnType("text").IsRequired();
            b.Property(x => x.Prefix).HasColumnName("prefix").HasColumnType("text").IsRequired();
            b.Property(x => x.SecretHash).HasColumnName("secret_hash").HasColumnType("text").IsRequired();
            b.HasIndex(x => x.SecretHash).IsUnique().HasDatabaseName("ix_public_keys_secret_hash");
            b.Property(x => x.Active).HasColumnName("active").HasDefaultValue(true);
            b.Property(x => x.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone");
            b.Property(x => x.MaxPerMinute).HasColumnName("max_per_minute");
            b.Property(x => x.CreatedAt).HasColumnName("created_at")
                .HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");
            b.Property(x => x.LastUsedAt).HasColumnName("last_used_at").HasColumnType("timestamp with time zone");
            b.HasIndex(x => x.EndpointId).HasDatabaseName("ix_public_keys_endpoint_id");
        });

        modelBuilder.Entity<ThirdPartyUsageRow>(b =>
        {
            b.ToTable("public_usage");
            b.HasKey(x => new { x.KeyId, x.Minute }).HasName("public_usage_pkey");
            b.Property(x => x.KeyId).HasColumnName("key_id");
            b.Property(x => x.Minute).HasColumnName("minute").HasColumnType("timestamp with time zone");
            b.Property(x => x.Count).HasColumnName("count");
            b.HasOne(x => x.Key).WithMany().HasForeignKey(x => x.KeyId)
                .HasConstraintName("public_usage_key_id_fkey").OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ThirdPartyAuditRow>(b =>
        {
            b.ToTable("public_audit");
            b.HasKey(x => x.Id).HasName("public_audit_pkey");
            b.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            b.Property(x => x.EndpointId).HasColumnName("endpoint_id");
            b.Property(x => x.KeyId).HasColumnName("key_id");
            b.Property(x => x.Method).HasColumnName("method").HasColumnType("text").IsRequired();
            b.Property(x => x.HttpStatus).HasColumnName("http_status");
            b.Property(x => x.DurationMs).HasColumnName("duration_ms");
            b.Property(x => x.CreatedAt).HasColumnName("created_at")
                .HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");
            b.HasIndex(x => new { x.EndpointId, x.CreatedAt })
                .HasDatabaseName("idx_public_audit_endpoint").IsDescending(false, true);
        });
    }
}
