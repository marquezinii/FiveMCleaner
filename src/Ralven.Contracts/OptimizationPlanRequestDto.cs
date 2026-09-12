namespace Ralven.Contracts;

public sealed record OptimizationPlanRequestDto
{
    public OptimizationScope Scope { get; init; } = OptimizationScope.FiveMLegacy;

    public required OptimizationProfile Profile { get; init; }

    public required FiveMEdition Edition { get; init; }

    public OptimizationOptionsDto Options { get; init; } = new();

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PersonalOptimizationPreferencesDto? PersonalPreferences { get; init; }

    /// <summary>
    /// Detected Windows client version. Actions that do not support it are
    /// excluded from the plan. Defaults to <see cref="SupportedWindowsVersions.All"/>,
    /// which keeps every catalog action eligible.
    /// </summary>
    public SupportedWindowsVersions DetectedWindows { get; init; } = SupportedWindowsVersions.All;
}
