using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Dhole.ApiGateway.ThirdParty.Migrations;

[DbContext(typeof(ThirdPartyDbContext))]
public sealed class ThirdPartyDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
        => ThirdPartyInitialModel.Build(modelBuilder);
}
