using FiveMCleaner.Contracts;

namespace FiveMCleaner.Core.Catalog;

public sealed class OptimizationActionDefinition
{
    internal OptimizationActionDefinition(
        string id,
        int version,
        string name,
        string description,
        ActionCategory category,
        ActionRisk risk,
        ActionReversibility reversibility,
        RequiredPrivilege requiredPrivilege,
        IReadOnlyList<OptimizationProfile> supportedProfiles,
        bool requiresFiveMStopped,
        bool requiresAcPower,
        bool requiresRestart,
        int progressWeight,
        string expectedImpact,
        ActionOptionGate optionGate)
    {
        Id = id;
        Version = version;
        Name = name;
        Description = description;
        Category = category;
        Risk = risk;
        Reversibility = reversibility;
        RequiredPrivilege = requiredPrivilege;
        SupportedProfiles = supportedProfiles;
        RequiresFiveMStopped = requiresFiveMStopped;
        RequiresAcPower = requiresAcPower;
        RequiresRestart = requiresRestart;
        ProgressWeight = progressWeight;
        ExpectedImpact = expectedImpact;
        OptionGate = optionGate;
    }

    public string Id { get; }

    public int Version { get; }

    public string Name { get; }

    public string Description { get; }

    public ActionCategory Category { get; }

    public ActionRisk Risk { get; }

    public ActionReversibility Reversibility { get; }

    public RequiredPrivilege RequiredPrivilege { get; }

    public IReadOnlyList<OptimizationProfile> SupportedProfiles { get; }

    public bool RequiresFiveMStopped { get; }

    public bool RequiresAcPower { get; }

    public bool RequiresRestart { get; }

    public int ProgressWeight { get; }

    public string ExpectedImpact { get; }

    internal ActionOptionGate OptionGate { get; }

    public bool Supports(OptimizationProfile profile)
    {
        return SupportedProfiles.Contains(profile);
    }

    public ActionMetadataDto ToMetadata()
    {
        return new ActionMetadataDto
        {
            Id = Id,
            Version = Version,
            Name = Name,
            Description = Description,
            Category = Category,
            SupportedProfiles = SupportedProfiles.ToArray(),
            Risk = Risk,
            Reversibility = Reversibility,
            RequiredPrivilege = RequiredPrivilege,
            RequiresFiveMStopped = RequiresFiveMStopped,
            RequiresAcPower = RequiresAcPower,
            RequiresRestart = RequiresRestart,
            ProgressWeight = ProgressWeight,
            ExpectedImpact = ExpectedImpact
        };
    }
}

internal enum ActionOptionGate
{
    Always,
    CleanUserTemporaryFiles,
    RemoveOldFiveMCrashDumps,
    RepairLegacyServerCache,
    EnableGameMode,
    PreferHighPerformanceGpu,
    DisableBackgroundCapture,
    UseSessionPerformancePowerPlan,
    ApplyLegacyGraphicsPreset,
    ApplyGtaVGraphicsPreset,
    ReduceWindowsVisualEffects
}
