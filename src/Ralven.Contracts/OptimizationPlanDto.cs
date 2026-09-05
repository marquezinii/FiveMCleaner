namespace Ralven.Contracts;

public sealed record PlanBlockDto
{
    public required PlanBlockCode Code { get; init; }

    public required string Message { get; init; }
}

public sealed record PlanNoticeDto
{
    public required string Code { get; init; }

    public required PlanNoticeSeverity Severity { get; init; }

    public required string Message { get; init; }

    public string? ActionId { get; init; }
}

public sealed record PlannedActionDto
{
    public required int Sequence { get; init; }

    public required ActionMetadataDto Metadata { get; init; }
}

public sealed record OptimizationPlanDto
{
    public OptimizationScope Scope { get; init; } = OptimizationScope.FiveMLegacy;

    public required Guid PlanId { get; init; }

    public required int SchemaVersion { get; init; }

    public required int CatalogVersion { get; init; }

    public required string ProductName { get; init; }

    public required string ProductSubtitle { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required OptimizationProfile Profile { get; init; }

    public required FiveMEdition Edition { get; init; }

    public required OptimizationOptionsDto Options { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PersonalOptimizationPreferencesDto? PersonalPreferences { get; init; }

    public required bool IsExecutable { get; init; }

    public required bool RequiresElevation { get; init; }

    public required bool ContainsNonReversibleActions { get; init; }

    public required ActionRisk MaximumRisk { get; init; }

    public required IReadOnlyList<PlannedActionDto> Actions { get; init; }

    public required IReadOnlyList<PlanBlockDto> Blocks { get; init; }

    public required IReadOnlyList<PlanNoticeDto> Notices { get; init; }
}
