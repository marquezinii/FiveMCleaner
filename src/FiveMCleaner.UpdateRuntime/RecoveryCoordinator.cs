namespace FiveMCleaner.UpdateRuntime;

public enum RecoveryDecision { Healthy, RolledBack, Pending }

public sealed class RecoveryCoordinator
{
    private readonly RuntimeActivationStore activation;
    private readonly UpdateRecoveryJournal journal;
    private readonly UpdateHealthReceiptStore receipt;
    public RecoveryCoordinator(string runtimeRoot)
    {
        activation = new RuntimeActivationStore(runtimeRoot);
        journal = new UpdateRecoveryJournal(runtimeRoot);
        receipt = new UpdateHealthReceiptStore(runtimeRoot);
    }

    public RecoveryDecision Reconcile()
    {
        var transaction = journal.Read();
        if (receipt.Confirms(transaction)) return RecoveryDecision.Healthy;
        if (activation.ReadActiveVersion() != transaction.CandidateVersion) return RecoveryDecision.Pending;
        activation.Activate(transaction.PreviousVersion);
        return RecoveryDecision.RolledBack;
    }
}
