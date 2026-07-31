using FiveMCleaner.UpdateRuntime;
using Xunit;

namespace FiveMCleaner.Tests.UpdateRuntime;

public sealed class RecoveryCoordinatorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FiveMCleanerRecoveryCoordinator", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public void Reconcile_RestoresOnlyTheRecordedPredecessorWhenHealthIsMissing()
    {
        var runtime = new RuntimeActivationStore(root);
        Directory.CreateDirectory(Path.Combine(runtime.VersionsRoot, "1.0.0"));
        Directory.CreateDirectory(Path.Combine(runtime.VersionsRoot, "1.1.0"));
        runtime.Activate("1.0.0");
        var transaction = new UpdateRecoveryJournal(root).Begin("1.0.0", "1.1.0");
        runtime.Activate(transaction.CandidateVersion);

        Assert.Equal(RecoveryDecision.RolledBack, new RecoveryCoordinator(root).Reconcile());
        Assert.Equal("1.0.0", runtime.ReadActiveVersion());
    }
}
