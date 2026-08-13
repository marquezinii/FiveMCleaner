using System.Collections.ObjectModel;
using FiveMCleaner.Contracts;

namespace FiveMCleaner.Core.Catalog;

public sealed partial class ActionCatalog
{
    public const int CurrentVersion = 14;

    private static readonly string[] NoPrerequisites = [];
    private static readonly string[] RequiresFiveMStoppedFirst = [OptimizationActionIds.VerifyFiveMIsStopped];
    private static readonly string[] RequiresGtaVStoppedFirst = [OptimizationActionIds.VerifyGtaVIsStopped];

    private static readonly OptimizationProfile[] AllProfiles =
    [
        OptimizationProfile.Light,
        OptimizationProfile.Balanced,
        OptimizationProfile.Aggressive
    ];

    private static readonly OptimizationProfile[] BalancedAndAggressive =
    [
        OptimizationProfile.Balanced,
        OptimizationProfile.Aggressive
    ];

    private static readonly OptimizationProfile[] AggressiveOnly =
    [
        OptimizationProfile.Aggressive
    ];

    private readonly IReadOnlyDictionary<string, OptimizationActionDefinition> _byId;

    private ActionCatalog(IReadOnlyList<OptimizationActionDefinition> actions)
    {
        Actions = new ReadOnlyCollection<OptimizationActionDefinition>(actions.ToArray());
        _byId = new ReadOnlyDictionary<string, OptimizationActionDefinition>(
            actions.ToDictionary(action => action.Id, StringComparer.Ordinal));
    }

    public static ActionCatalog Current { get; } = new(CreateActions());

    public IReadOnlyList<OptimizationActionDefinition> Actions { get; }

    public bool TryGet(string actionId, out OptimizationActionDefinition? definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        return _byId.TryGetValue(actionId, out definition);
    }

    public OptimizationActionDefinition GetRequired(string actionId)
    {
        return TryGet(actionId, out var definition)
            ? definition!
            : throw new KeyNotFoundException($"Unknown optimization action ID '{actionId}'.");
    }

    private static IReadOnlyList<OptimizationActionDefinition> CreateActions()
    {
        return
        [
            .. CreateVerificationAndBottleneckActions(),
            .. CreateHardwareDiagnosisActions(),
            .. CreateGraphicsDiagnosisActions(),
            .. CreateCleanupActions(),
            .. CreateGamingAndPowerActions(),
            .. CreateGraphicsPresetActions(),
            .. CreateGamingGuidanceActions(),
            .. CreateAppearanceActions()
        ];
    }

    private static OptimizationActionDefinition Define(
        string id,
        string name,
        string description,
        ActionCategory category,
        ActionRisk risk,
        ActionReversibility reversibility,
        RequiredPrivilege requiredPrivilege,
        IReadOnlyList<OptimizationProfile> supportedProfiles,
        bool requiresFiveMStopped,
        int progressWeight,
        string expectedImpact,
        ActionOptionGate optionGate,
        bool requiresAcPower = false,
        bool requiresRestart = false,
        IReadOnlyList<string>? prerequisites = null,
        bool isCritical = false,
        SupportedWindowsVersions supportedWindows = SupportedWindowsVersions.All,
        string detectionSummary = "",
        string confirmationSummary = "",
        string undoSummary = "",
        string riskLimitations = "",
        bool attemptWithoutElevationFirst = false)
    {
        return new OptimizationActionDefinition(
            id,
            version: 1,
            name,
            description,
            category,
            risk,
            reversibility,
            requiredPrivilege,
            supportedProfiles.ToArray(),
            requiresFiveMStopped,
            requiresAcPower,
            requiresRestart,
            progressWeight,
            expectedImpact,
            optionGate,
            prerequisites ?? NoPrerequisites,
            isCritical,
            supportedWindows,
            detectionSummary,
            confirmationSummary,
            undoSummary,
            riskLimitations,
            attemptWithoutElevationFirst);
    }
}
