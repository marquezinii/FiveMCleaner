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
}
