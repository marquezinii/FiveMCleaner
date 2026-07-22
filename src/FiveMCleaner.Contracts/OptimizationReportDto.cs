namespace FiveMCleaner.Contracts;

/// <summary>One action's line in the final optimization report.</summary>
public sealed record OptimizationReportLineDto
{
    public required int Sequence { get; init; }

    public required string ActionId { get; init; }

    public required string ActionName { get; init; }

    public required ActionCategory Category { get; init; }

    public required ActionExecutionOutcome Outcome { get; init; }

    public string? Reason { get; init; }
}

/// <summary>
/// Structured, honest summary of an optimization run, built from the local
/// journal. Distinguishes verified, changed, skipped, warning and failed
/// actions and never claims full success when any action failed.
/// </summary>
public sealed record OptimizationReportDto
{
    public required Guid TransactionId { get; init; }

    public required OptimizationProfile Profile { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required int VerifiedCount { get; init; }

    public required int ChangedCount { get; init; }

    public required int SkippedCount { get; init; }

    public required int WarningCount { get; init; }

    public required int FailedCount { get; init; }

    public required int RollbackFailedCount { get; init; }

    public required int NotRunCount { get; init; }

    public required bool RequiresRestart { get; init; }

    public required bool RestorePossible { get; init; }

    /// <summary>True only when no action failed or failed to roll back.</summary>
    public required bool Succeeded { get; init; }

    public required IReadOnlyList<OptimizationReportLineDto> Lines { get; init; }
}
