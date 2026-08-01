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

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
