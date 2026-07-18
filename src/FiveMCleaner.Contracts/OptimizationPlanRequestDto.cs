namespace FiveMCleaner.Contracts;

public sealed record OptimizationPlanRequestDto
{
    public required OptimizationProfile Profile { get; init; }

    public required FiveMEdition Edition { get; init; }

    public OptimizationOptionsDto Options { get; init; } = new();
}
