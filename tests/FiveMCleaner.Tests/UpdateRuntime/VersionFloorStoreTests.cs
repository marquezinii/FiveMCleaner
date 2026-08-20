using FiveMCleaner.UpdateRuntime;
using Xunit;

namespace FiveMCleaner.Tests.UpdateRuntime;

public sealed class VersionFloorStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "FiveMCleanerVersionFloor", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Advance_PersistsTheHighestConfirmedVersion()
    {
        var store = new VersionFloorStore(root);
        Assert.Equal("1.0.0", store.Read("1.0.0"));

        store.Advance("2.0.0");
        store.Advance("1.5.0");

        Assert.Equal("2.0.0", store.Read("1.0.0"));
    }

    [Fact]
    public void Advance_RejectsDowngradeAfterFloorIsSet()
    {
        var store = new VersionFloorStore(root);
        store.Advance("2.0.0");

        store.Advance("1.0.0"); // Should silently reject
        Assert.Equal("2.0.0", store.Read("1.0.0"));
    }

    [Fact]
    public void Advance_AllowsAnyVersionOnFirstRun()
    {
        var store = new VersionFloorStore(root);
        store.Advance("1.0.0");
        Assert.Equal("1.0.0", store.Read("1.0.0"));

        store.Advance("0.9.0"); // Should silently reject after floor is set
        Assert.Equal("1.0.0", store.Read("1.0.0"));
    }

    [Fact]
    public void Advance_AllowsSameVersion()
    {
        var store = new VersionFloorStore(root);
        store.Advance("2.0.0");
        store.Advance("2.0.0");
        Assert.Equal("2.0.0", store.Read("1.0.0"));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
