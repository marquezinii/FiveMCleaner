using FiveMCleaner.Contracts;

namespace FiveMCleaner.App.Services;

public sealed record AppDiagnostic
{
    public required FiveMEdition Edition { get; init; }

    public required bool IsFiveMRunning { get; init; }

    public string? FiveMRoot { get; init; }

    public required string CpuName { get; init; }

    public required string GpuName { get; init; }

    public required double TotalMemoryGiB { get; init; }

    public required double FreeDiskGiB { get; init; }

    public required long LegacyCacheBytes { get; init; }

    public required string OsLabel { get; init; }

    public required int ReadinessScore { get; init; }

    public required OptimizationProfile RecommendedProfile { get; init; }

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
    public bool CleanTemporaryFiles { get; init; } = true;

    public bool RemoveOldDiagnostics { get; init; } = true;

    public bool SmartCacheRepair { get; init; }

    public bool EnableGameMode { get; init; } = true;

    public bool DisableBackgroundCapture { get; init; } = true;

    public bool UsePerformancePowerPlan { get; init; } = true;
}
