namespace FiveMCleaner.Contracts;

public sealed record OptimizationOptionsDto
{
    public bool CleanUserTemporaryFiles { get; init; } = true;

    public int TemporaryFileMinimumAgeDays { get; init; } = 7;

    public bool RemoveOldFiveMCrashDumps { get; init; } = true;

    public int DiagnosticRetentionDays { get; init; } = 14;

    public CacheRepairPolicy ServerCacheRepair { get; init; } = CacheRepairPolicy.Off;

    public int ServerCacheThresholdGiB { get; init; } = 8;

    public bool EnableGameMode { get; init; } = true;

    public bool PreferHighPerformanceGpu { get; init; } = true;

    public bool DisableBackgroundCapture { get; init; } = true;

    public bool UseSessionPerformancePowerPlan { get; init; } = true;

    public bool ApplyLegacyGraphicsPreset { get; init; } = true;

    public bool ApplyGtaVGraphicsPreset { get; init; }

    public bool ReduceWindowsVisualEffects { get; init; } = true;

    /// <summary>
    /// Opt-in repair action, never part of automatic profile composition
    /// (see docs/safety.md). Off by default; only meant to be turned on for
    /// a specific, manually-requested repair run.
    /// </summary>
    public bool TerminateStuckFiveMProcess { get; init; }

    /// <summary>
    /// Opt-in repair action, never part of automatic profile composition
    /// (see docs/safety.md). Off by default; only meant to be turned on for
    /// a specific, manually-requested repair run.
    /// </summary>
    public bool RecreateFiveMLocalData { get; init; }

    /// <summary>
    /// Opt-in repair action, never part of automatic profile composition
    /// (see docs/safety.md). Off by default; only meant to be turned on for
    /// a specific, manually-requested repair run, and even then only removes
    /// data when the action's own detection confirms the specific error
    /// pattern is present.
    /// </summary>
    public bool RepairStaleAuthData { get; init; }
}
