using FiveMCleaner.UpdateRuntime;
using Xunit;

namespace FiveMCleaner.Tests.UpdateRuntime;

public sealed class UpdateHealthReceiptStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FiveMCleanerHealth", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public void Confirm_AcceptsOnlyItsMatchingTransaction()
    {
        var journal = new UpdateRecoveryJournal(root);
        var transaction = journal.Begin("1.0.0", "1.1.0");
        var receipt = new UpdateHealthReceiptStore(root);
        receipt.Confirm(transaction);
        Assert.True(receipt.Confirms(transaction));
        Assert.False(receipt.Confirms(transaction with { Nonce = "other" }));
    }

    [Fact]
    public void Confirms_TreatsATransientLockAsNotConfirmedInsteadOfThrowing()
    {
        var journal = new UpdateRecoveryJournal(root);
        var transaction = journal.Begin("1.0.0", "1.1.0");
        var receipt = new UpdateHealthReceiptStore(root);
        receipt.Confirm(transaction);
        var path = Path.Combine(root, "health.json");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(receipt.Confirms(transaction));
        }

        Assert.True(receipt.Confirms(transaction));
    }
}
