using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Dhole.ApiGateway.ThirdParty.Migrations;

[DbContext(typeof(ThirdPartyDbContext))]
[Migration("20261102170000_InitialThirdPartyStorage")]
public sealed class InitialThirdPartyStorage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "integrations");

        migrationBuilder.CreateTable(
            name: "public_endpoints",
            schema: "integrations",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                slug = table.Column<string>(type: "text", nullable: false),
                document = table.Column<string>(type: "jsonb", nullable: false),
                enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table => table.PrimaryKey("public_endpoints_pkey", x => x.id));

        migrationBuilder.CreateTable(
            name: "public_audit",
            schema: "integrations",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                key_id = table.Column<Guid>(type: "uuid", nullable: false),
                method = table.Column<string>(type: "text", nullable: false),
                http_status = table.Column<int>(type: "integer", nullable: false),
                duration_ms = table.Column<long>(type: "bigint", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table => table.PrimaryKey("public_audit_pkey", x => x.id));

        migrationBuilder.CreateTable(
            name: "public_keys",
            schema: "integrations",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                label = table.Column<string>(type: "text", nullable: false),
                prefix = table.Column<string>(type: "text", nullable: false),
                secret_hash = table.Column<string>(type: "text", nullable: false),
                active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                max_per_minute = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("public_keys_pkey", x => x.id);
                table.ForeignKey(
                    name: "public_keys_endpoint_id_fkey",
                    column: x => x.endpoint_id,
                    principalSchema: "integrations",
                    principalTable: "public_endpoints",
                    principalColumn: "id");
            });

        migrationBuilder.CreateTable(
            name: "public_usage",
            schema: "integrations",
            columns: table => new
            {
                key_id = table.Column<Guid>(type: "uuid", nullable: false),
                minute = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                count = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("public_usage_pkey", x => new { x.key_id, x.minute });
                table.ForeignKey(
                    name: "public_usage_key_id_fkey",
                    column: x => x.key_id,
                    principalSchema: "integrations",
                    principalTable: "public_keys",
                    principalColumn: "id");
            });

        migrationBuilder.CreateIndex(
            name: "ix_public_endpoints_slug",
            schema: "integrations",
            table: "public_endpoints",
            column: "slug",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "ix_public_keys_endpoint_id",
            schema: "integrations",
            table: "public_keys",
            column: "endpoint_id");
        migrationBuilder.CreateIndex(
            name: "ix_public_keys_secret_hash",
            schema: "integrations",
            table: "public_keys",
            column: "secret_hash",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "idx_public_audit_endpoint",
            schema: "integrations",
            table: "public_audit",
            columns: new[] { "endpoint_id", "created_at" },
            descending: new[] { false, true });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "public_audit", schema: "integrations");
        migrationBuilder.DropTable(name: "public_usage", schema: "integrations");
        migrationBuilder.DropTable(name: "public_keys", schema: "integrations");
        migrationBuilder.DropTable(name: "public_endpoints", schema: "integrations");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
        => ThirdPartyInitialModel.Build(modelBuilder);
}
