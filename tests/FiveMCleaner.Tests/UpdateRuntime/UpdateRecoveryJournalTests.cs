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
}
