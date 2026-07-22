using FiveMCleaner.Contracts;

namespace FiveMCleaner.App.Services;

public enum AppThemePreference
{
    System,
    Dark,
    Light
}

public enum PerformancePressureLevel
{
    Low,
    Moderate,
    High
}

public sealed record AppDiagnostic
{
    public required FiveMEdition Edition { get; init; }

    public required bool IsFiveMRunning { get; init; }

    public string? FiveMRoot { get; init; }

    public required bool GtaVDetected { get; init; }

    public required bool GtaVIsRunning { get; init; }

    public string? GtaVExecutablePath { get; init; }

    public required string GtaVGraphicsSettingsPath { get; init; }

    public required string CpuName { get; init; }

    public required string GpuName { get; init; }

    public required double TotalMemoryGiB { get; init; }

    public required double AvailableMemoryGiB { get; init; }

    public required int LogicalProcessorCount { get; init; }

    public required double FreeDiskGiB { get; init; }

    public required long LegacyCacheBytes { get; init; }

    public required string OsLabel { get; init; }

    public required int ReadinessScore { get; init; }

    public required OptimizationProfile RecommendedProfile { get; init; }

    public required PerformancePressureLevel PerformancePressure { get; init; }

    public required StreamingSoftwareSnapshot StreamingSoftware { get; init; }

    public IReadOnlyList<string> Notices { get; init; } = [];
}

public enum AppProgressKind
{
    Preparing,
    Applying,
    Verifying,
    Completed,
    Warning,
    Failed,
    RollingBack
}

public sealed record AppProgressUpdate
{
    public required DateTimeOffset Timestamp { get; init; }

    public required AppProgressKind Kind { get; init; }

    public required double Percent { get; init; }

    public required string Headline { get; init; }

    public required string Detail { get; init; }

    public string? ActionId { get; init; }
}

public sealed record AppOptimizationResult
{
    public required Guid TransactionId { get; init; }

    public required bool Succeeded { get; init; }

    public required bool WasCancelled { get; init; }

    public required string Summary { get; init; }

    public required int CompletedActions { get; init; }

    public required long BytesFreed { get; init; }
}

public sealed record AppHistoryRecord
{
    public required Guid TransactionId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required OptimizationProfile Profile { get; init; }

    public required string State { get; init; }

    public required int ChangedActions { get; init; }

    public required bool CanRollback { get; init; }
}

public sealed record AppSettings
{
    public AppLanguagePreference Language { get; init; } = AppLanguagePreference.Automatic;

    public AppThemePreference Theme { get; init; } = AppThemePreference.System;

    public bool MinimizeToTrayOnClose { get; init; }

    public bool LaunchAtStartup { get; init; }

    public bool CheckForUpdates { get; init; } = true;
}
