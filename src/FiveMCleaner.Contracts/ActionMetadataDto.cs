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

    /// <summary>
    /// When true, an administrator-privileged action is first attempted
    /// without elevation (many Windows configurations allow a standard user
    /// to perform it); only a genuine access-denied result defers it to the
    /// elevated broker phase. Ignored for <see cref="RequiredPrivilege.StandardUser"/>
    /// actions.
    /// </summary>
    public bool AttemptWithoutElevationFirst { get; init; }

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

    /// <summary>
    /// Compares every member of the contract, including the risk,
    /// reversibility and prerequisite metadata that governs elevation and
    /// rollback.
    /// </summary>
    /// <remarks>
    /// The record's generated equality cannot be used here: the list members
    /// compare by reference, so two structurally identical instances that were
    /// deserialized separately would not be equal. Both the elevated broker
    /// and the Windows catalog reject a plan whose metadata drifted from the
    /// local catalog, and restating the field list at each boundary let the
    /// two checks fall out of sync. Keeping the comparison on the contract
    /// makes a newly added member impossible to forget.
    /// </remarks>
    public bool MatchesExactly(ActionMetadataDto other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return string.Equals(Id, other.Id, StringComparison.Ordinal)
            && Version == other.Version
            && string.Equals(Name, other.Name, StringComparison.Ordinal)
            && string.Equals(Description, other.Description, StringComparison.Ordinal)
            && Category == other.Category
            && SupportedProfiles is not null
            && other.SupportedProfiles is not null
            && SupportedProfiles.SequenceEqual(other.SupportedProfiles)
            && Risk == other.Risk
            && Reversibility == other.Reversibility
            && RequiredPrivilege == other.RequiredPrivilege
            && RequiresFiveMStopped == other.RequiresFiveMStopped
            && RequiresAcPower == other.RequiresAcPower
            && RequiresRestart == other.RequiresRestart
            && ProgressWeight == other.ProgressWeight
            && string.Equals(ExpectedImpact, other.ExpectedImpact, StringComparison.Ordinal)
            && Prerequisites is not null
            && other.Prerequisites is not null
            && Prerequisites.SequenceEqual(other.Prerequisites, StringComparer.Ordinal)
            && IsCritical == other.IsCritical
            && AttemptWithoutElevationFirst == other.AttemptWithoutElevationFirst
            && SupportedWindows == other.SupportedWindows
            && string.Equals(DetectionSummary, other.DetectionSummary, StringComparison.Ordinal)
            && string.Equals(ConfirmationSummary, other.ConfirmationSummary, StringComparison.Ordinal)
            && string.Equals(UndoSummary, other.UndoSummary, StringComparison.Ordinal)
            && string.Equals(RiskLimitations, other.RiskLimitations, StringComparison.Ordinal);
    }
}
