using System.Text.Json;
using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Planning;
using Xunit;

namespace FiveMCleaner.Tests.Core;

public sealed class ContractJsonTests
{
    [Fact]
    public void Request_RoundTripsWithCamelCaseStringEnums()
    {
        var request = new OptimizationPlanRequestDto
        {
            Profile = OptimizationProfile.Balanced,
            Edition = FiveMEdition.Legacy,
            Options = new OptimizationOptionsDto
            {
                ServerCacheRepair = CacheRepairPolicy.WhenOversized,
                ServerCacheThresholdGiB = 12
            }
        };

        var json = FiveMCleanerJson.SerializeRequest(request);
        var restored = FiveMCleanerJson.DeserializeRequest(json);

        Assert.Contains("\"profile\":\"balanced\"", json, StringComparison.Ordinal);
        Assert.Contains("\"edition\":\"legacy\"", json, StringComparison.Ordinal);
        Assert.Contains("\"serverCacheRepair\":\"whenOversized\"", json, StringComparison.Ordinal);
        Assert.Equal(request, restored);
    }

    [Fact]
    public void Plan_RoundTripsWithActionMetadata()
    {
        var original = new PlanBuilder().Build(new OptimizationPlanRequestDto
        {
            Profile = OptimizationProfile.Aggressive,
            Edition = FiveMEdition.Legacy
        });

        var json = FiveMCleanerJson.SerializePlan(original);
        var restored = FiveMCleanerJson.DeserializePlan(json);

        Assert.Equal(original.PlanId, restored.PlanId);
        Assert.Equal(original.SchemaVersion, restored.SchemaVersion);
        Assert.Equal(original.CatalogVersion, restored.CatalogVersion);
        Assert.Equal(original.ProductName, restored.ProductName);
        Assert.Equal(original.ProductSubtitle, restored.ProductSubtitle);
        Assert.Equal(original.Profile, restored.Profile);
        Assert.Equal(original.Edition, restored.Edition);
        Assert.Equal(
            original.Actions.Select(action => action.Metadata.Id),
            restored.Actions.Select(action => action.Metadata.Id));
    }

    [Fact]
    public void UnknownJsonMembers_AreRejected()
    {
        const string json = """
            {
              "profile": "light",
              "edition": "legacy",
              "options": {},
              "command": "powershell -encodedCommand unsafe"
            }
            """;

        Assert.Throws<JsonException>(() => FiveMCleanerJson.DeserializeRequest(json));
    }

    [Fact]
    public void NumericEnums_AreRejected()
    {
        const string json = """
            {
              "profile": 1,
              "edition": "legacy",
              "options": {}
            }
            """;

        Assert.Throws<JsonException>(() => FiveMCleanerJson.DeserializeRequest(json));
    }

    [Fact]
    public void EmptyPayload_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => FiveMCleanerJson.DeserializeRequest(" "));
    }
}
