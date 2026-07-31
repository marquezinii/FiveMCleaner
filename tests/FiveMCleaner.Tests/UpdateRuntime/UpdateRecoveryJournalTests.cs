using FiveMCleaner.UpdateRuntime;
using Xunit;

namespace FiveMCleaner.Tests.UpdateRuntime;

public sealed class UpdateRecoveryJournalTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FiveMCleanerRecovery", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public void Begin_PersistsOnlyTheExactRollbackPredecessor()
    {
        var journal = new UpdateRecoveryJournal(root);
        var transaction = journal.Begin("1.0.0", "1.1.0");
        Assert.Equal(transaction, journal.Read());
        Assert.Throws<ArgumentException>(() => journal.Begin("1.1.0", "1.0.0"));
    }

    [Fact]
    public void TryRead_TreatsATransientLockAsNoJournalWithoutQuarantiningTheFile()
    {
        var journal = new UpdateRecoveryJournal(root);
        journal.Begin("1.0.0", "1.1.0");
        var path = Path.Combine(root, "recovery.json");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(journal.TryRead(out _));
        }

        Assert.True(File.Exists(path));
        Assert.True(journal.TryRead(out var transaction));
        Assert.Equal("1.1.0", transaction.CandidateVersion);
    }
}
