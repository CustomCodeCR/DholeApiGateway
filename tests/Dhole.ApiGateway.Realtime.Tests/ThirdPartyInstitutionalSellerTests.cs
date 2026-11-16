using Dhole.ApiGateway.ThirdParty;
using Xunit;

namespace Dhole.ApiGateway.Realtime.Tests;

public sealed class ThirdPartyInstitutionalSellerTests
{
    [Fact]
    public void Seller_user_id_is_optional_because_pricing_uses_authenticated_actor()
    {
        var fields = ThirdPartyFieldCatalog.Inputs("pricing-rate-request-create");
        var seller = Assert.Single(fields, field => field.Internal == "sellerUserId");
        Assert.False(seller.Required);
        Assert.False(seller.Selected);
        Assert.Contains(fields, field => field.Internal == "clientName");
        Assert.Contains(fields, field => field.Internal == "executiveName");
    }
}
