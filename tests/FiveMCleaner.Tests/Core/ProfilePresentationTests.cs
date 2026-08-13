using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using Xunit;

namespace FiveMCleaner.Tests.Core;

public sealed class ProfilePresentationTests
{
    [Theory]
    [InlineData(OptimizationProfile.Light, ProfileImpactLevel.Low)]
    [InlineData(OptimizationProfile.Balanced, ProfileImpactLevel.Moderate)]
    [InlineData(OptimizationProfile.Aggressive, ProfileImpactLevel.High)]
    public void For_MapsImpactLevelPerProfile(OptimizationProfile profile, ProfileImpactLevel expected)
    {
        var presentation = ProfilePresentationProvider.For(profile);

        Assert.Equal(profile, presentation.Profile);
        Assert.Equal(expected, presentation.ImpactLevel);
    }

    [Theory]
    [InlineData(OptimizationProfile.Light)]
    [InlineData(OptimizationProfile.Balanced)]
    [InlineData(OptimizationProfile.Aggressive)]
    public void For_DerivesProfileFactsFromCatalogSoTheyCannotDrift(
        OptimizationProfile profile)
    {
        var presentation = ProfilePresentationProvider.For(profile);

        var actions = ActionCatalog.Current.Actions
            .Where(action => action.Supports(profile))
            .ToArray();
        var expectedCategories = actions
            .Select(action => action.Category)
            .Distinct()
            .OrderBy(category => (int)category)
            .ToArray();

        Assert.Equal(expectedCategories, presentation.AnalyzedCategories);
        Assert.NotEmpty(presentation.AnalyzedCategories);
        Assert.Equal(
            actions.Any(action => action.Reversibility is ActionReversibility.Irreversible
                or ActionReversibility.RebuildableData),
            presentation.ContainsNonReversible);
        Assert.Equal(
            actions.Any(action => action.RequiredPrivilege == RequiredPrivilege.Administrator),
            presentation.RequiresElevation);
        Assert.Equal(actions.Max(action => action.Risk), presentation.MaximumRisk);
    }

    [Fact]
    public void For_RejectsUndefinedProfile()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ProfilePresentationProvider.For((OptimizationProfile)99));
    }
}
