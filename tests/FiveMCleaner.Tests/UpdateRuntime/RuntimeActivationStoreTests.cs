using FiveMCleaner.UpdateRuntime;
using Xunit;

namespace FiveMCleaner.Tests.UpdateRuntime;

public sealed class RuntimeActivationStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FiveMCleanerRuntime", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public void Activate_SwapsOnlyThePointerBetweenStagedVersions()
    {
        var store = new RuntimeActivationStore(root);
        Directory.CreateDirectory(Path.Combine(store.VersionsRoot, "1.0.0"));
        Directory.CreateDirectory(Path.Combine(store.VersionsRoot, "1.1.0"));
        store.Activate("1.0.0");
        store.Activate("1.1.0");
        Assert.Equal("1.1.0", store.ReadActiveVersion());
        Assert.True(Directory.Exists(Path.Combine(store.VersionsRoot, "1.0.0")));
    }
}
