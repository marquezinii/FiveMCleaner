using FiveMCleaner.Contracts;

namespace FiveMCleaner.Core.Catalog;

public enum ProfileImpactLevel
{
    Low,
    Moderate,
    High
}

/// <summary>
/// Structural facts about an optimization mode, derived from the action
/// catalog so the presentation can never drift from what actually runs. The
/// human-facing text (benefits, risks, variability note) is localized by the
/// UI; these facts anchor it to the real plan.
/// </summary>
public sealed record OptimizationProfilePresentation
{
    public required OptimizationProfile Profile { get; init; }

    public required ProfileImpactLevel ImpactLevel { get; init; }

    /// <summary>Distinct categories analyzed for this profile, ordered.</summary>
    public required IReadOnlyList<ActionCategory> AnalyzedCategories { get; init; }

    /// <summary>True when the profile can include irreversible/rebuildable actions.</summary>
    public required bool ContainsNonReversible { get; init; }

    /// <summary>True when the profile can require an administrator prompt.</summary>
    public required bool RequiresElevation { get; init; }

    /// <summary>The highest action risk the profile can reach.</summary>
    public required ActionRisk MaximumRisk { get; init; }
}

public static class ProfilePresentationProvider
{
    public static OptimizationProfilePresentation For(
        OptimizationProfile profile,
        ActionCatalog? catalog = null)
    {
        if (!Enum.IsDefined(profile))
        {
            throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown optimization profile.");
        }

        catalog ??= ActionCatalog.Current;
        var actions = catalog.Actions.Where(action => action.Supports(profile)).ToArray();

        var categories = actions
            .Select(action => action.Category)
            .Distinct()
            .OrderBy(category => (int)category)
            .ToArray();

        return new OptimizationProfilePresentation
        {
            Profile = profile,
            ImpactLevel = profile switch
            {
                OptimizationProfile.Light => ProfileImpactLevel.Low,
                OptimizationProfile.Balanced => ProfileImpactLevel.Moderate,
                OptimizationProfile.Aggressive => ProfileImpactLevel.High,
                _ => ProfileImpactLevel.Moderate
            },
            AnalyzedCategories = categories,
            ContainsNonReversible = actions.Any(action =>
                action.Reversibility is ActionReversibility.Irreversible
                    or ActionReversibility.RebuildableData),
            RequiresElevation = actions.Any(action =>
                action.RequiredPrivilege == RequiredPrivilege.Administrator),
            MaximumRisk = actions.Length == 0
                ? ActionRisk.Informational
                : actions.Max(action => action.Risk)
        };
    }
}
