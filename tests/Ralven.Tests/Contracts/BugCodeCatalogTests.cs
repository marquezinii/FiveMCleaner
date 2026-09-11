using System.Linq;
using Ralven.Contracts;
using Xunit;

namespace Ralven.Tests.Contracts;

public sealed class BugCodeCatalogTests
{
    [Theory]
    [InlineData(BugCode.BRK_ACTION_EXECUTION, "BRK", "BugCode.Category.Broker")]
    [InlineData(BugCode.APP_INV_SCAN, "APP", "BugCode.Category.App")]
    [InlineData(BugCode.SEC_HEALTH_QUERY, "SEC", "BugCode.Category.Security")]
    [InlineData(BugCode.FIVEM_CACHE_OPERATION, "FIVEM", "BugCode.Category.FiveM")]
    public void GetCategory_And_GetCategoryResourceKey_MatchKnownCodes(
        BugCode code, string expectedCategory, string expectedResourceKey)
    {
        Assert.Equal(expectedCategory, BugCodeCatalog.GetCategory(code));
        Assert.Equal(expectedResourceKey, BugCodeCatalog.GetCategoryResourceKey(code));
    }

    [Fact]
    public void GetCategoryResourceKey_EveryDefinedBugCode_ResolvesToAKnownCategory()
    {
        foreach (var code in Enum.GetValues<BugCode>())
        {
            if (code == BugCode.Unknown) continue;

            Assert.NotNull(BugCodeCatalog.GetCategoryResourceKey(code));
        }
    }
}
