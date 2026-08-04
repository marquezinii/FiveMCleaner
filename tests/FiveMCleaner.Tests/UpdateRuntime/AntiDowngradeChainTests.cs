using System.IO;
using System.Security.Cryptography;
using FiveMCleaner.UpdateRuntime;
using Xunit;

namespace FiveMCleaner.Tests.UpdateRuntime;

public sealed class AntiDowngradeChainTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "FiveMCleanerAntiDowngrade", Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public void FloorStore_AdvancePersistsAndNeverGoesDown()
    {
        var store = new VersionFloorStore(root);
        Assert.Equal("1.0.0", store.Read("1.0.0"));
        store.Advance("2.0.0");
        Assert.Equal("2.0.0", store.Read("1.0.0"));
        // Attempted downgrade must not overwrite the persisted floor
        store.Advance("1.5.0");
        Assert.Equal("2.0.0", store.Read("1.0.0"));
    }

    [Fact]
    public void FloorStore_CorruptDpapiThrows()
    {
        var store = new VersionFloorStore(root);
        store.Advance("2.0.0");
        var floorPath = Path.Combine(root, "UpdateSecurity", "version-floor.dpapi");
        Assert.True(File.Exists(floorPath));
        // Corrupt the DPAPI blob
        var bytes = File.ReadAllBytes(floorPath);
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(floorPath, bytes);
        Assert.Throws<CryptographicException>(() => store.Read("1.0.0"));
    }

    [Fact]
    public void FloorStore_PersistsEvenWhenFallbackVersionDiffers()
    {
        var store = new VersionFloorStore(root);
        store.Advance("3.0.0");
        // Fallback version is lower; floor persists correctly
        Assert.Equal("3.0.0", store.Read("1.0.0"));
        Assert.Equal("3.0.0", store.Read("2.5.0"));
    }

    [Fact]
    public void RecoveryRestoresPredecessor_AndFloorRemainsHighestConfirmed()
    {
        var runtimeRoot = Path.Combine(root, "runtime");
        var dataRoot = Path.Combine(root, "data");
        var runtime = new RuntimeActivationStore(runtimeRoot);
        CreateVersion(runtime, "1.0.0");
        CreateVersion(runtime, "2.0.0");
        runtime.Activate("1.0.0");

        var floor = new VersionFloorStore(dataRoot);
        floor.Advance("1.0.0");

        // Advance to 2.0.0, begin journal, mark launched, confirm floor
        runtime.Activate("2.0.0");
        var journal = new UpdateRecoveryJournal(runtimeRoot);
        var transaction = journal.Begin("1.0.0", "2.0.0");
        transaction = journal.MarkCandidateLaunched(transaction);
        floor.Advance("2.0.0");

        // Health timeout: candidate launched, no receipt, far past timeout
        var farFuture = transaction.CandidateLaunchedAtUtc!.Value.AddMinutes(10);
        var decision = new RecoveryCoordinator(runtimeRoot).Reconcile(farFuture, TimeSpan.Zero);
        Assert.Equal(RecoveryDecision.RolledBack, decision);

        // active.json now points to 1.0.0; journal is complete
        Assert.Equal("1.0.0", runtime.ReadActiveVersion());
        Assert.False(journal.TryRead(out _));

        // Floor holds the highest confirmed version (2.0.0) above active (1.0.0)
        Assert.Equal("2.0.0", floor.Read("1.0.0"));
        var activeVersion = Version.Parse(runtime.ReadActiveVersion());
        var floorVersion = Version.Parse(floor.Read("1.0.0"));
        Assert.True(activeVersion < floorVersion);
    }

    private static void CreateVersion(RuntimeActivationStore store, string version)
        => Directory.CreateDirectory(Path.Combine(store.VersionsRoot, version));
}
