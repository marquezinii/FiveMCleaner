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

public enum ActionReversibility
{
    ReadOnly,
    FullyReversible,
    SessionScoped,
    RebuildableData,
    Irreversible
}

public enum RequiredPrivilege
{
    StandardUser,
    Administrator
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
public enum ActionExecutionOutcome
{
    /// <summary>The action has not produced a result yet.</summary>
    Pending,

    /// <summary>The machine already matched the desired state; nothing was written.</summary>
    Verified,

    /// <summary>The change was applied and its post-condition confirmed.</summary>
    Applied,

    /// <summary>A precondition, option or path was absent; skipped without error.</summary>
    Skipped,

    /// <summary>Applied with a caveat or reportable partial success.</summary>
    Warning,

    /// <summary>A genuine error occurred; the action reverted itself.</summary>
    Failed,

    /// <summary>The action reverted successfully after a failure.</summary>
    RolledBack,

    /// <summary>The action could not revert and needs attention.</summary>
    RollbackFailed,

    /// <summary>The edition or safety context does not support the action.</summary>
    Blocked,

    /// <summary>The action did not run because an earlier critical failure aborted the run.</summary>
    NotRun
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
