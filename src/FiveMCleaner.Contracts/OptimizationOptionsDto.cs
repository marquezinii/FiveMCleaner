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

    /// <summary>
    /// Opt-in preset, never part of automatic profile composition. Raises
    /// (never lowers) existing graphics options up to a conservative ceiling.
    /// </summary>
    public bool ApplyQualityGraphicsPreset { get; init; }

    /// <summary>
    /// Opt-in preference, never part of automatic profile composition. Only
    /// touches windowed mode and VSync; never resolution/refresh/adapter.
    /// </summary>
    public bool ApplyDisplayPreferences { get; init; }

    /// <summary>Desired windowed mode when <see cref="ApplyDisplayPreferences"/> is enabled.</summary>
    public bool PreferWindowedMode { get; init; }

    /// <summary>Desired VSync state when <see cref="ApplyDisplayPreferences"/> is enabled.</summary>
    public bool EnableVSync { get; init; } = true;

    /// <summary>
    /// Opt-in, standalone GTA V only (FiveM ignores commandline.txt — see
    /// docs/research.md). Writes -cityDensity/-anisotropicQualityLevel/
    /// -fxaa/-grassQuality/-lodScale/-frameLimit. Never part of automatic
    /// profile composition.
    /// </summary>
    public bool ApplyGtaVGraphicsLaunchParameters { get; init; }

    /// <summary>
    /// Opt-in, standalone GTA V only. Writes -fullscreen/-windowed/
    /// -borderless and, when set, -DX10/-DX10_1/-DX11. Never part of
    /// automatic profile composition.
    /// </summary>
    public bool ApplyGtaVDisplayLaunchParameters { get; init; }

    /// <summary>When enabled with <see cref="ApplyGtaVDisplayLaunchParameters"/>, uses -borderless instead of -windowed/-fullscreen.</summary>
    public bool PreferBorderlessWindow { get; init; }

    /// <summary>DirectX version to write, or <see cref="GtaVDirectXVersion.Unspecified"/> to let the game auto-detect.</summary>
    public GtaVDirectXVersion GtaVLaunchDirectXVersion { get; init; } = GtaVDirectXVersion.Unspecified;

    /// <summary>
    /// Opt-in, standalone GTA V only. Writes temporary repair parameters
    /// (-safemode/-useMinimumSettings/-UseAutoSettings). Never part of
    /// automatic profile composition; must be reverted after diagnosing.
    /// </summary>
    public bool ApplyGtaVRepairLaunchParameters { get; init; }

    public bool UseGtaVSafeMode { get; init; }

    public bool UseGtaVMinimumSettings { get; init; }

    public bool UseGtaVAutoSettingsRebuild { get; init; }
}
