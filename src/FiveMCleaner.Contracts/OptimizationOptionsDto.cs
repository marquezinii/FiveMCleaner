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
    /// Opt-in preference for FiveM Legacy's gta5_settings.xml, never part of
    /// automatic profile composition. Only touches windowed mode and VSync;
    /// never resolution/refresh/adapter. Independent from
    /// <see cref="ApplyGtaVDisplayPreferences"/> — enabling one must not
    /// silently enable the other.
    /// </summary>
    public bool ApplyDisplayPreferences { get; init; }

    /// <summary>
    /// Opt-in preference for standalone GTA V Legacy's settings.xml, never
    /// part of automatic profile composition. Only touches windowed mode and
    /// VSync; never resolution/refresh/adapter. Independent from
    /// <see cref="ApplyDisplayPreferences"/>.
    /// </summary>
    public bool ApplyGtaVDisplayPreferences { get; init; }

    /// <summary>Desired windowed mode when <see cref="ApplyDisplayPreferences"/> or <see cref="ApplyGtaVDisplayPreferences"/> is enabled.</summary>
    public bool PreferWindowedMode { get; init; }

    /// <summary>Desired VSync state when <see cref="ApplyDisplayPreferences"/> or <see cref="ApplyGtaVDisplayPreferences"/> is enabled.</summary>
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

    /// <summary>
    /// Opt-in experiment, Aggressive profile only, never part of automatic
    /// profile composition. Toggles the "Disable fullscreen optimizations"
    /// compatibility flag for FiveM/GTA V -- a compatibility test, not a
    /// guaranteed improvement (Microsoft's own guidance is that Fullscreen
    /// Optimizations perform the same or better on average). Fully
    /// reversible; the user is expected to compare and revert manually if
    /// there is no improvement.
    /// </summary>
    public bool ToggleFullscreenOptimizationsExperiment { get; init; }

    /// <summary>
    /// Opt-in experiment, Aggressive profile only, never part of automatic
    /// profile composition. Flips Hardware-Accelerated GPU Scheduling
    /// (HAGS) to whichever state the machine is not currently using.
    /// Requires a Windows restart to take effect and never presented as a
    /// guaranteed FPS gain. Fully reversible.
    /// </summary>
    public bool ToggleHagsExperiment { get; init; }

    /// <summary>
    /// Opt-in repair guidance, never part of automatic profile composition.
    /// Off by default; only meant to be turned on when the user suspects
    /// display driver corruption and wants the guided clean-reinstall steps
    /// -- never touches any driver file itself.
    /// </summary>
    public bool GuideDriverReinstall { get; init; }
}
