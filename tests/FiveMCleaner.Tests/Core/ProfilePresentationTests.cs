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

    [Fact]
    public void For_DerivesCategoriesFromCatalogSoItCannotDrift()
    {
        var presentation = ProfilePresentationProvider.For(OptimizationProfile.Aggressive);

        var expected = ActionCatalog.Current.Actions
            .Where(action => action.Supports(OptimizationProfile.Aggressive))
            .Select(action => action.Category)
            .Distinct()
            .OrderBy(category => (int)category)
            .ToArray();

        Assert.Equal(expected, presentation.AnalyzedCategories);
        Assert.NotEmpty(presentation.AnalyzedCategories);
    }

    [Fact]
    public void For_FlagsElevationAndReversibilityHonestly()
    {
        // O perfil equilibrado inclui o plano de energia (administrador, reversível).
        var balanced = ProfilePresentationProvider.For(OptimizationProfile.Balanced);
        Assert.True(balanced.RequiresElevation);

        // O perfil leve não deve exigir elevação.
        var light = ProfilePresentationProvider.For(OptimizationProfile.Light);
        Assert.False(light.RequiresElevation);
    }

    [Fact]
    public void For_RejectsUndefinedProfile()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ProfilePresentationProvider.For((OptimizationProfile)99));
    }
}
