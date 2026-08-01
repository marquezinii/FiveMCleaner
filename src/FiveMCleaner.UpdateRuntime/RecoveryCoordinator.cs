using System.Text.Json;

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
        string activeVersion;
        try
        {
            activeVersion = activation.ReadActiveVersion();
        }
        // A transient lock on active.json (concurrent Activate() elsewhere, an AV
        // scan) is not proof the pointer is broken; defer this reconciliation
        // instead of letting the exception surface as a launch failure.
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or JsonException or InvalidDataException)
        {
            return RecoveryDecision.Pending;
        }
        if (activeVersion != transaction.CandidateVersion) return RecoveryDecision.Pending;
        if (transaction.CandidateLaunchedAtUtc is null
            || nowUtc - transaction.CandidateLaunchedAtUtc < healthTimeout) return RecoveryDecision.Pending;
        activation.Activate(transaction.PreviousVersion);
        journal.Complete();
        return RecoveryDecision.RolledBack;
    }

    /// <summary>
    /// Reverts an update whose candidate was activated but never even
    /// launched -- e.g. the previous process did not exit in time, or any
    /// other failure struck before <c>Process.Start</c>. <see cref="Reconcile"/>
    /// deliberately never rolls back a candidate with no
    /// <see cref="UpdateTransaction.CandidateLaunchedAtUtc"/>, because that
    /// guard exists to let a just-started candidate finish its own health
    /// timeout; here there is no running candidate to wait for, so leaving
    /// <c>active.json</c> pointed at a version that never ran would stick
    /// the next launch attempt with it. Safe to call for a transaction whose
    /// candidate never launched even if the active pointer no longer matches
    /// it (e.g. a previous call already reverted it) -- reactivating is
    /// skipped in that case, and the journal entry is simply completed.
    /// </summary>
    public void Abandon(UpdateTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (activation.ReadActiveVersion() == transaction.CandidateVersion)
            activation.Activate(transaction.PreviousVersion);
        journal.Complete();
    }
}
