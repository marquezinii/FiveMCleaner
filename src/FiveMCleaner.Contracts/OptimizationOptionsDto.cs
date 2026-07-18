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

    public bool ReduceWindowsVisualEffects { get; init; } = true;
}
