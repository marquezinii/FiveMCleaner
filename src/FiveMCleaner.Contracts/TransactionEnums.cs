namespace FiveMCleaner.Contracts;

/// <summary>
/// Lifecycle of an optimization transaction as a whole.
/// </summary>
/// <remarks>
/// DURABLE CONTRACT. Persisted by name (camelCase) into
/// <c>%LOCALAPPDATA%\FiveMCleaner\Transactions\{id}.json</c>, which outlives the
/// application version that wrote it and is the only record that keeps a past
/// run auditable and reversible. Renaming, removing or renumbering a member
/// makes existing journals unreadable and silently destroys the user's ability
/// to roll back. Members may only be appended.
/// </remarks>
public enum TransactionState
{
    /// <summary>The journal exists; no action has been attempted yet.</summary>
    Created = 0,

    /// <summary>Actions are being applied.</summary>
    Applying = 1,

    /// <summary>Applied actions are being confirmed and finalized.</summary>
    Committing = 2,

    /// <summary>Every action completed without a genuine failure.</summary>
    Committed = 3,

    /// <summary>The run finished, but at least one action failed or could not revert.</summary>
    CommittedWithErrors = 4,

    /// <summary>Remaining work needs the elevated broker before it can continue.</summary>
    AwaitingElevation = 5,

    /// <summary>A rollback of elevated work is pending the elevated broker.</summary>
    AwaitingElevationRollback = 6,

    /// <summary>A rollback is pending in the standard-privilege process.</summary>
    AwaitingStandardRollback = 7,

    /// <summary>A rollback is in progress.</summary>
    RollingBack = 8,

    /// <summary>Every applied change was reverted successfully.</summary>
    RolledBack = 9,

    /// <summary>The transaction failed before completing.</summary>
    Failed = 10,

    /// <summary>The rollback itself failed; the machine needs attention.</summary>
    RollbackFailed = 11
}

/// <summary>
/// Low-level state of a single action inside a transaction. Drives the
/// transactional machine and rollback eligibility, and is deliberately
/// independent from <see cref="ActionExecutionOutcome"/>, which is the semantic
/// result shown to the user.
/// </summary>
/// <remarks>
/// DURABLE CONTRACT. Persisted by name alongside <see cref="TransactionState"/>;
/// the same append-only rule applies.
/// </remarks>
public enum ActionJournalState
{
    /// <summary>Recorded in the plan; not attempted yet.</summary>
    Pending = 0,

    /// <summary>Skipped because the required privilege was unavailable.</summary>
    SkippedPrivilege = 1,

    /// <summary>Deferred to the elevated broker phase.</summary>
    DeferredPrivilege = 2,

    /// <summary>The change is being written.</summary>
    Applying = 3,

    /// <summary>The change was written and a snapshot exists for rollback.</summary>
    Applied = 4,

    /// <summary>The change is being confirmed.</summary>
    Committing = 5,

    /// <summary>The change was confirmed and finalized.</summary>
    Committed = 6,

    /// <summary>A precondition was absent; nothing was written.</summary>
    Skipped = 7,

    /// <summary>The change is being reverted.</summary>
    RollingBack = 8,

    /// <summary>The change was reverted successfully.</summary>
    RolledBack = 9,

    /// <summary>The action failed.</summary>
    Failed = 10,

    /// <summary>The action could not be reverted.</summary>
    RollbackFailed = 11
}
