namespace FiveMCleaner.Contracts;

public enum OptimizationProfile
{
    Light,
    Balanced,
    Aggressive
}

public enum FiveMEdition
{
    Unknown,
    Legacy,
    Enhanced
}

public enum ActionCategory
{
    Safety,
    Storage,
    WindowsGaming,
    Power,
    Appearance,
    FiveMGraphics
}

public enum ActionRisk
{
    Informational,
    Low,
    Moderate,
    High
}

/// <summary>
/// How far a single action can be undone once it has been applied. Drives the
/// rollback ordering, the non-reversible warning shown before a plan runs and
/// the report's restore flag.
/// </summary>
/// <remarks>
/// DURABLE CONTRACT. Persisted by name (camelCase) on every action entry of
/// <c>%LOCALAPPDATA%\FiveMCleaner\Transactions\{id}.json</c>, so a rollback
/// performed by a later build still reads the reversibility recorded when the
/// change was made. Members may be appended, never renamed or removed.
/// </remarks>
public enum ActionReversibility
{
    /// <summary>Diagnostic only; nothing is written, so nothing needs undoing.</summary>
    ReadOnly = 0,

    /// <summary>A snapshot restores the previous value exactly.</summary>
    FullyReversible = 1,

    /// <summary>Removed data the system rebuilds on demand, such as a cache.</summary>
    RebuildableData = 2,

    /// <summary>Data is removed for good; the plan must warn before running it.</summary>
    Irreversible = 3
}

/// <summary>Privilege an action needs in order to run.</summary>
/// <remarks>
/// DURABLE CONTRACT. Persisted by name alongside <see cref="ActionReversibility"/>
/// in the transaction journal; the same append-only rule applies.
/// </remarks>
public enum RequiredPrivilege
{
    /// <summary>Runs in the normal, unelevated application process.</summary>
    StandardUser = 0,

    /// <summary>Deferred to the elevated broker when it cannot run unelevated.</summary>
    Administrator = 1
}

public enum CacheRepairPolicy
{
    Off,
    WhenOversized,
    RepairNow
}

/// <summary>
/// DirectX version election for standalone GTA V's <c>commandline.txt</c>
/// (<c>-DX10</c>/<c>-DX10_1</c>/<c>-DX11</c>). <c>Unspecified</c> means the
/// flag is not written at all, letting the game auto-detect as it does by
/// default.
/// </summary>
public enum GtaVDirectXVersion
{
    Unspecified,
    DX10,
    DX10_1,
    DX11
}

public enum PlanBlockCode
{
    EditionNotDetected,
    EnhancedNotSupported
}

public enum PlanNoticeSeverity
{
    Information,
    Warning
}

/// <summary>
/// Semantic result of a single optimization action after the engine verified,
/// applied, validated and recorded it. Distinct from the low-level journal
/// state so reports and the UI can present outcomes honestly.
/// </summary>
/// <remarks>
/// DURABLE CONTRACT. Persisted by name into the transaction journal (see
/// <see cref="TransactionState"/>). Members may be appended, never renamed,
/// removed or renumbered.
/// </remarks>
public enum ActionExecutionOutcome
{
    /// <summary>The action has not produced a result yet.</summary>
    Pending = 0,

    /// <summary>The machine already matched the desired state; nothing was written.</summary>
    Verified = 1,

    /// <summary>The change was applied and its post-condition confirmed.</summary>
    Applied = 2,

    /// <summary>A precondition, option or path was absent; skipped without error.</summary>
    Skipped = 3,

    /// <summary>Applied with a caveat or reportable partial success.</summary>
    Warning = 4,

    /// <summary>A genuine error occurred; the action reverted itself.</summary>
    Failed = 5,

    /// <summary>The action reverted successfully after a failure.</summary>
    RolledBack = 6,

    /// <summary>The action could not revert and needs attention.</summary>
    RollbackFailed = 7,

    /// <summary>The action did not run because an earlier critical failure aborted the run.</summary>
    NotRun = 8
}

/// <summary>
/// Windows client versions an action supports. Actions that only make sense on a
/// specific version are gated by the detected OS before entering a plan.
/// </summary>
[Flags]
public enum SupportedWindowsVersions
{
    None = 0,
    Windows10 = 1,
    Windows11 = 2,
    All = Windows10 | Windows11
}
