namespace Ralven.Contracts;

public enum PersonalUsage
{
    Everyday = 0,
    Gaming = 1,
    Streaming = 2,
    Work = 3
}

/// <summary>User intent, never a list of executable actions or registry paths.</summary>
public sealed record PersonalOptimizationPreferencesDto
{
    public PersonalUsage Usage { get; init; }

    public bool PreserveAppearance { get; init; } = true;

    public bool PreserveBackgroundCapture { get; init; } = true;

    public bool AllowPerformancePower { get; init; }

    public bool CleanOldTemporaryFiles { get; init; }

    public bool UseConsistentPointerResponse { get; init; }
}
