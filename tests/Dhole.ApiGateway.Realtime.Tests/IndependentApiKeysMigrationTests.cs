using Dhole.ApiGateway.ThirdParty.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Dhole.ApiGateway.Realtime.Tests;

/// <summary>
/// Regression: positional ForeignKey arguments incorrectly targeted PostgreSQL schema "id"
/// and caused SQLSTATE 3F000 when migrating integrations.public_key_endpoints.
/// </summary>
public sealed class IndependentApiKeysMigrationTests
{
    [Fact]
    public void ForeignKeys_reference_integrations_schema_and_correct_principal_tables()
    {
        var migration = new IndependentApiKeys();
        var table = Assert.Single(migration.UpOperations
            .OfType<CreateTableOperation>()
            .Where(operation => operation.Name == "public_key_endpoints"));

        Assert.Equal("integrations", table.Schema);
        Assert.Equal(2, table.ForeignKeys.Count);

        var key = Assert.Single(table.ForeignKeys.Where(fk => fk.Name == "public_key_endpoints_key_id_fkey"));
        Assert.Equal("integrations", key.PrincipalSchema);
        Assert.Equal("public_keys", key.PrincipalTable);
        Assert.Equal(new[] { "id" }, key.PrincipalColumns);

        var endpoint = Assert.Single(table.ForeignKeys.Where(fk => fk.Name == "public_key_endpoints_endpoint_id_fkey"));
        Assert.Equal("integrations", endpoint.PrincipalSchema);
        Assert.Equal("public_endpoints", endpoint.PrincipalTable);
        Assert.Equal(new[] { "id" }, endpoint.PrincipalColumns);
    }
}
