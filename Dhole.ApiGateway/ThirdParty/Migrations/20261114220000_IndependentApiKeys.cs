using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Dhole.ApiGateway.ThirdParty.Migrations;

[DbContext(typeof(ThirdPartyDbContext))]
[Migration("20261114220000_IndependentApiKeys")]
public sealed class IndependentApiKeys : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<Guid>(
            name: "endpoint_id", schema: "integrations", table: "public_keys",
            type: "uuid", nullable: true, oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.CreateTable(
            name: "public_key_endpoints", schema: "integrations",
            columns: table => new
            {
                key_id = table.Column<Guid>(type: "uuid", nullable: false),
                endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("public_key_endpoints_pkey", x => new { x.key_id, x.endpoint_id });
                table.ForeignKey(
                    name: "public_key_endpoints_key_id_fkey",
                    column: x => x.key_id,
                    principalTable: "public_keys",
                    principalColumn: "id",
                    principalSchema: "integrations",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "public_key_endpoints_endpoint_id_fkey",
                    column: x => x.endpoint_id,
                    principalTable: "public_endpoints",
                    principalColumn: "id",
                    principalSchema: "integrations",
                    onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex(
            name: "ix_public_key_endpoints_endpoint_id",
            schema: "integrations", table: "public_key_endpoints", column: "endpoint_id");

        // Existing per-endpoint secrets remain linked and usable after migration.
        migrationBuilder.Sql("""
            INSERT INTO integrations.public_key_endpoints(key_id, endpoint_id)
            SELECT id, endpoint_id FROM integrations.public_keys WHERE endpoint_id IS NOT NULL
            ON CONFLICT DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "public_key_endpoints", schema: "integrations");
        // Standalone keys have no historical endpoint FK, so they cannot exist in the old model.
        migrationBuilder.Sql("DELETE FROM integrations.public_keys WHERE endpoint_id IS NULL;");
        migrationBuilder.AlterColumn<Guid>(
            name: "endpoint_id", schema: "integrations", table: "public_keys",
            type: "uuid", nullable: false, oldClrType: typeof(Guid),
            oldType: "uuid", oldNullable: true);
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
        => ThirdPartyCurrentModel.Build(modelBuilder);
}
