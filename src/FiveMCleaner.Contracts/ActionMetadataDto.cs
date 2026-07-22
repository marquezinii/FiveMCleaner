namespace FiveMCleaner.Contracts;

public sealed record ActionMetadataDto
{
    public required string Id { get; init; }

    public required int Version { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required ActionCategory Category { get; init; }

    public required IReadOnlyList<OptimizationProfile> SupportedProfiles { get; init; }

    public required ActionRisk Risk { get; init; }

    public required ActionReversibility Reversibility { get; init; }

    public required RequiredPrivilege RequiredPrivilege { get; init; }

    public required bool RequiresFiveMStopped { get; init; }

    public required bool RequiresAcPower { get; init; }

    public required bool RequiresRestart { get; init; }

    public required int ProgressWeight { get; init; }

    public required string ExpectedImpact { get; init; }

    /// <summary>
    /// Action IDs whose successful result (Verified or Applied) is required
    /// before this action may run. If a prerequisite did not succeed, this
    /// action is skipped rather than attempted.
    /// </summary>
    public IReadOnlyList<string> Prerequisites { get; init; } = [];

    /// <summary>
    /// When true, a genuine failure of this action aborts the remaining
    /// independent actions in the run because continuing could be unsafe.
    /// </summary>
    public bool IsCritical { get; init; }

    /// <summary>Windows client versions on which this action is meaningful.</summary>
    public SupportedWindowsVersions SupportedWindows { get; init; } = SupportedWindowsVersions.All;

    /// <summary>How the engine detects whether the change is already in place.</summary>
    public string DetectionSummary { get; init; } = string.Empty;

    /// <summary>How the engine confirms the change actually took effect.</summary>
    public string ConfirmationSummary { get; init; } = string.Empty;

    /// <summary>How the change can be undone.</summary>
    public string UndoSummary { get; init; } = string.Empty;

    /// <summary>Known risks and limitations of the action.</summary>
    public string RiskLimitations { get; init; } = string.Empty;
}
