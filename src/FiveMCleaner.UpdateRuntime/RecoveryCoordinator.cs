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

    public RecoveryDecision Reconcile(DateTimeOffset nowUtc, TimeSpan healthTimeout)
    {
        if (!journal.TryRead(out var transaction)) return RecoveryDecision.Healthy;
        if (receipt.Confirms(transaction))
        {
            journal.Complete();
            return RecoveryDecision.Healthy;
        }
        if (activation.ReadActiveVersion() != transaction.CandidateVersion) return RecoveryDecision.Pending;
        if (transaction.CandidateLaunchedAtUtc is null
            || nowUtc - transaction.CandidateLaunchedAtUtc < healthTimeout) return RecoveryDecision.Pending;
        activation.Activate(transaction.PreviousVersion);
        journal.Complete();
        return RecoveryDecision.RolledBack;
    }
}
