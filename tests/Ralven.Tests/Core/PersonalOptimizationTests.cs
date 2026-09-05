using System.Text.Json;
using Ralven.Contracts;
using Ralven.Core.Catalog;
using Ralven.Core.Planning;
using Xunit;

namespace Ralven.Tests.Core;

public sealed class PersonalOptimizationTests
{
    [Theory]
    [InlineData(PersonalUsage.Everyday, false)]
    [InlineData(PersonalUsage.Gaming, true)]
    [InlineData(PersonalUsage.Streaming, true)]
    [InlineData(PersonalUsage.Work, false)]
    public void DefaultRoutinePreservesAppearanceCaptureAndBattery(PersonalUsage usage, bool gameMode)
    {
        var plan = Build(new() { Usage = usage });
        var ids = plan.Actions.Select(action => action.Metadata.Id).ToArray();

        Assert.True(plan.IsExecutable);
        Assert.Equal(gameMode, plan.Options.EnableGameMode);
        Assert.False(plan.Options.DisableBackgroundCapture);
        Assert.False(plan.Options.ReduceWindowsVisualEffects);
        Assert.False(plan.Options.UseSessionPerformancePowerPlan);
        Assert.False(plan.Options.CleanUserTemporaryFiles);
        Assert.False(plan.Options.AdjustPciExpressPowerManagement);
        Assert.DoesNotContain(OptimizationActionIds.DisableMouseAcceleration, ids);
        Assert.DoesNotContain(OptimizationActionIds.ApplyAggressiveLegacyGraphics, ids);
        Assert.DoesNotContain(OptimizationActionIds.ApplyAggressiveGtaVGraphics, ids);
        Assert.DoesNotContain(plan.Notices, notice => notice.Code == "aggressive-prioritizes-performance");
        Assert.Equal(plan.Options, CreatePlan(PlanBuilder.CanonicalRequestFor(plan)).Options);
    }

    [Fact]
    public void ExplicitPreferencesOnlyEnableSupportedActions()
    {
        var plan = Build(new()
        {
            Usage = PersonalUsage.Streaming,
            PreserveAppearance = false,
            PreserveBackgroundCapture = false,
            AllowPerformancePower = true,
            CleanOldTemporaryFiles = true,
            UseConsistentPointerResponse = true
        });
        var ids = plan.Actions.Select(action => action.Metadata.Id).ToArray();

        Assert.Contains(OptimizationActionIds.ReduceWindowsVisualEffects, ids);
        Assert.Contains(OptimizationActionIds.DisableBackgroundCapture, ids);
        Assert.Contains(OptimizationActionIds.EnableSessionPerformancePowerPlan, ids);
        Assert.Contains(OptimizationActionIds.CleanUserTemporaryFiles, ids);
        Assert.Contains(OptimizationActionIds.DisableMouseAcceleration, ids);
        Assert.Equal(30, plan.Options.TemporaryFileMinimumAgeDays);
        Assert.False(plan.Options.AdjustPciExpressPowerManagement);
    }

    [Fact]
    public void SmartRecommendationAdaptsToRoutinePressureAndStreamingSoftware()
    {
        var gaming = PersonalOptimizationPolicy.Recommend(
            PersonalUsage.Gaming,
            highPerformancePressure: true,
            streamingSoftwareDetected: false);
        Assert.False(gaming.PreserveAppearance);
        Assert.False(gaming.PreserveBackgroundCapture);
        Assert.True(gaming.AllowPerformancePower);
        Assert.True(gaming.UseConsistentPointerResponse);
        Assert.False(gaming.CleanOldTemporaryFiles);

        var streamingDetected = PersonalOptimizationPolicy.Recommend(
            PersonalUsage.Gaming,
            highPerformancePressure: false,
            streamingSoftwareDetected: true);
        Assert.True(streamingDetected.PreserveAppearance);
        Assert.True(streamingDetected.PreserveBackgroundCapture);

        var work = PersonalOptimizationPolicy.Recommend(
            PersonalUsage.Work,
            highPerformancePressure: false,
            streamingSoftwareDetected: false);
        Assert.False(work.PreserveBackgroundCapture);
        Assert.False(work.AllowPerformancePower);
        Assert.False(work.UseConsistentPointerResponse);
    }

    [Fact]
    public void PersonalPreferencesCannotExpandScopeOrInjectOptions()
    {
        var request = new OptimizationPlanRequestDto
        {
            Scope = OptimizationScope.GeneralWindows,
            Profile = OptimizationProfile.Aggressive,
            Edition = FiveMEdition.Unknown,
            PersonalPreferences = new(),
            Options = new() { AdjustPciExpressPowerManagement = true, ReduceWindowsVisualEffects = true }
        };
        Assert.False(CreatePlan(request).Options.AdjustPciExpressPowerManagement);
        Assert.False(CreatePlan(request).Options.ReduceWindowsVisualEffects);
        Assert.Throws<ArgumentException>(() => CreatePlan(request with { Scope = OptimizationScope.FiveMLegacy }));
        Assert.Throws<ArgumentException>(() => CreatePlan(request with { Profile = OptimizationProfile.Light }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(new() { Usage = (PersonalUsage)99 }));
    }

    [Fact]
    public void OrdinaryPlanJsonKeepsExistingShapeAndPersonalPlansRoundTrip()
    {
        var personal = Build(new() { Usage = PersonalUsage.Work });
        var ordinary = CreatePlan(PlanBuilder.CanonicalRequestFor(personal) with { PersonalPreferences = null });
        Assert.DoesNotContain("personalPreferences", JsonSerializer.Serialize(ordinary, RalvenJson.Options));
        var roundTrip = JsonSerializer.Deserialize<OptimizationPlanDto>(
            JsonSerializer.Serialize(personal, RalvenJson.Options), RalvenJson.Options);
        Assert.Equal(personal.PersonalPreferences, roundTrip!.PersonalPreferences);
        Assert.Equal(personal.Options, roundTrip.Options);
    }

    private static OptimizationPlanDto CreatePlan(OptimizationPlanRequestDto request) => PlanBuilder.Build(request, PlanBuildContext.New(TimeProvider.System));

    private static OptimizationPlanDto Build(PersonalOptimizationPreferencesDto preferences) =>
        CreatePlan(new OptimizationPlanRequestDto
        {
            Scope = OptimizationScope.GeneralWindows,
            Profile = OptimizationProfile.Aggressive,
            Edition = FiveMEdition.Unknown,
            PersonalPreferences = preferences
        });
}
