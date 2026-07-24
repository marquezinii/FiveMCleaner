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
        ActionOptionGate optionGate,
        IReadOnlyList<string> prerequisites,
        bool isCritical,
        SupportedWindowsVersions supportedWindows,
        string detectionSummary,
        string confirmationSummary,
        string undoSummary,
        string riskLimitations)
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
        Prerequisites = prerequisites;
        IsCritical = isCritical;
        SupportedWindows = supportedWindows;
        DetectionSummary = detectionSummary;
        ConfirmationSummary = confirmationSummary;
        UndoSummary = undoSummary;
        RiskLimitations = riskLimitations;
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

    public IReadOnlyList<string> Prerequisites { get; }

    public bool IsCritical { get; }

    public SupportedWindowsVersions SupportedWindows { get; }

    public string DetectionSummary { get; }

    public string ConfirmationSummary { get; }

    public string UndoSummary { get; }

    public string RiskLimitations { get; }

    internal ActionOptionGate OptionGate { get; }

    public bool Supports(OptimizationProfile profile)
    {
        return SupportedProfiles.Contains(profile);
    }

    public bool SupportsWindows(SupportedWindowsVersions detected)
    {
        return detected == SupportedWindowsVersions.None
            || (SupportedWindows & detected) != SupportedWindowsVersions.None;
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
            ExpectedImpact = ExpectedImpact,
            Prerequisites = Prerequisites.ToArray(),
            IsCritical = IsCritical,
            SupportedWindows = SupportedWindows,
            DetectionSummary = DetectionSummary,
            ConfirmationSummary = ConfirmationSummary,
            UndoSummary = UndoSummary,
            RiskLimitations = RiskLimitations
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
    ReduceWindowsVisualEffects,
    TerminateStuckFiveMProcess,
    RecreateFiveMLocalData,
    RepairStaleAuthData,
    ApplyQualityGraphicsPreset,
    ApplyDisplayPreferences
}
