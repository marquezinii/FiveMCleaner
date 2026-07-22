using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using Xunit;

namespace FiveMCleaner.Tests.Core;

public sealed class ActionCatalogTests
{
    [Fact]
    public void CurrentCatalog_HasStableUniqueDefinitions()
    {
        var catalog = ActionCatalog.Current;

        Assert.Equal(2, ActionCatalog.CurrentVersion);
        Assert.NotEmpty(catalog.Actions);
        Assert.Equal(
            catalog.Actions.Count,
            catalog.Actions.Select(action => action.Id).Distinct(StringComparer.Ordinal).Count());

        Assert.All(catalog.Actions, action =>
        {
            Assert.False(string.IsNullOrWhiteSpace(action.Id));
            Assert.Equal(action.Id.Trim(), action.Id);
            Assert.Equal(action.Id.ToLowerInvariant(), action.Id);
            Assert.True(action.Version > 0);
            Assert.False(string.IsNullOrWhiteSpace(action.Name));
            Assert.False(string.IsNullOrWhiteSpace(action.Description));
            Assert.False(string.IsNullOrWhiteSpace(action.ExpectedImpact));
            Assert.True(action.ProgressWeight > 0);
            Assert.NotEmpty(action.SupportedProfiles);
            Assert.Equal(action.SupportedProfiles.Count, action.SupportedProfiles.Distinct().Count());
        });
    }

    [Theory]
    [InlineData(
        OptimizationActionIds.ApplyLightGtaVGraphics,
        OptimizationProfile.Light,
        ActionRisk.Low)]
    [InlineData(
        OptimizationActionIds.ApplyBalancedGtaVGraphics,
        OptimizationProfile.Balanced,
        ActionRisk.Moderate)]
    [InlineData(
        OptimizationActionIds.ApplyAggressiveGtaVGraphics,
        OptimizationProfile.Aggressive,
        ActionRisk.High)]
    public void GtaVGraphicsDefinitions_AreProfileSpecificAndReversible(
        string actionId,
        OptimizationProfile profile,
        ActionRisk risk)
    {
        var definition = ActionCatalog.Current.GetRequired(actionId);

        Assert.Equal([profile], definition.SupportedProfiles);
        Assert.Equal(risk, definition.Risk);
        Assert.Equal(ActionCategory.FiveMGraphics, definition.Category);
        Assert.Equal(ActionReversibility.FullyReversible, definition.Reversibility);
        Assert.Equal(RequiredPrivilege.StandardUser, definition.RequiredPrivilege);
        Assert.True(definition.RequiresFiveMStopped);
    }

    [Fact]
    public void GetRequired_RejectsUnknownActionIds()
    {
        Assert.Throws<KeyNotFoundException>(() =>
            ActionCatalog.Current.GetRequired("custom.command.from-untrusted-input"));
    }

    [Fact]
    public void ElevatedActions_AreExplicitlyMarkedAndReversible()
    {
        var elevated = ActionCatalog.Current.Actions
            .Where(action => action.RequiredPrivilege == RequiredPrivilege.Administrator)
            .ToArray();

        var powerAction = Assert.Single(elevated);
        Assert.Equal(OptimizationActionIds.EnableSessionPerformancePowerPlan, powerAction.Id);
        Assert.Equal(ActionReversibility.FullyReversible, powerAction.Reversibility);
        Assert.True(powerAction.RequiresAcPower);
    }

    [Fact]
    public void AggressiveProfile_DoesNotIntroduceUnknownOrUnsafeExecutionDescriptors()
    {
        var publicProperties = typeof(OptimizationActionDefinition)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        var unsafeExecutionProperties = new[]
        {
            "Command",
            "CommandLine",
            "Script",
            "Arguments",
            "ExecutablePath",
            "WorkingDirectory"
        };

        Assert.Empty(publicProperties.Intersect(unsafeExecutionProperties, StringComparer.OrdinalIgnoreCase));
    }
}
