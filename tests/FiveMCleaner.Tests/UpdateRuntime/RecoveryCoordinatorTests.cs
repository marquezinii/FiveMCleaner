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
        var journal = new UpdateRecoveryJournal(root);
        var transaction = journal.Begin("1.0.0", "1.1.0");
        runtime.Activate(transaction.CandidateVersion);

        Assert.Equal(RecoveryDecision.Pending, new RecoveryCoordinator(root).Reconcile(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)));
        transaction = journal.MarkCandidateLaunched(transaction);
        Assert.Equal(
            RecoveryDecision.RolledBack,
            new RecoveryCoordinator(root).Reconcile(transaction.CandidateLaunchedAtUtc!.Value.AddMinutes(2), TimeSpan.FromMinutes(1)));
        Assert.Equal("1.0.0", runtime.ReadActiveVersion());
    }

    [Fact]
    public void Abandon_RevertsACandidateThatWasActivatedButNeverLaunched()
    {
        var runtime = new RuntimeActivationStore(root);
        Directory.CreateDirectory(Path.Combine(runtime.VersionsRoot, "1.0.0"));
        Directory.CreateDirectory(Path.Combine(runtime.VersionsRoot, "1.1.0"));
        runtime.Activate("1.0.0");
        var journal = new UpdateRecoveryJournal(root);
        var transaction = journal.Begin("1.0.0", "1.1.0");
        runtime.Activate(transaction.CandidateVersion);

        // Reconcile alone must not roll this back -- it never launched, so
        // there is no health timeout to wait out (see the test above).
        Assert.Equal(RecoveryDecision.Pending, new RecoveryCoordinator(root).Reconcile(DateTimeOffset.UtcNow, TimeSpan.Zero));
        Assert.Equal("1.1.0", runtime.ReadActiveVersion());

        new RecoveryCoordinator(root).Abandon(transaction);

        Assert.Equal("1.0.0", runtime.ReadActiveVersion());
        Assert.False(journal.TryRead(out _));
    }

    [Fact]
    public void Abandon_IsSafeWhenTheActivePointerAlreadyMovedOn()
    {
        var runtime = new RuntimeActivationStore(root);
        Directory.CreateDirectory(Path.Combine(runtime.VersionsRoot, "1.0.0"));
        Directory.CreateDirectory(Path.Combine(runtime.VersionsRoot, "1.1.0"));
        Directory.CreateDirectory(Path.Combine(runtime.VersionsRoot, "1.2.0"));
        runtime.Activate("1.2.0");
        var journal = new UpdateRecoveryJournal(root);
        var transaction = journal.Begin("1.0.0", "1.1.0");

        new RecoveryCoordinator(root).Abandon(transaction);

        Assert.Equal("1.2.0", runtime.ReadActiveVersion());
        Assert.False(journal.TryRead(out _));
    }

    [Fact]
    public void Reconcile_DefersInsteadOfThrowingWhenActivePointerIsTransientlyLocked()
    {
        var runtime = new RuntimeActivationStore(root);
        Directory.CreateDirectory(Path.Combine(runtime.VersionsRoot, "1.0.0"));
        Directory.CreateDirectory(Path.Combine(runtime.VersionsRoot, "1.1.0"));
        runtime.Activate("1.0.0");
        var journal = new UpdateRecoveryJournal(root);
        journal.Begin("1.0.0", "1.1.0");
        var pointerPath = Path.Combine(root, "active.json");

        using (new FileStream(pointerPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(
                RecoveryDecision.Pending,
                new RecoveryCoordinator(root).Reconcile(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)));
        }
    }
}
