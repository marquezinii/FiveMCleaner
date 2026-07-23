using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Core.Planning;
using Xunit;

namespace FiveMCleaner.Tests.Core;

public sealed class PlanBuilderTests
{
    [Fact]
    public void LightProfile_UsesOnlyLowImpactStandardUserActions()
    {
        var plan = Build(OptimizationProfile.Light);

        Assert.True(plan.IsExecutable);
        Assert.False(plan.RequiresElevation);
        Assert.Equal(ActionRisk.Low, plan.MaximumRisk);
        Assert.True(plan.ContainsNonReversibleActions);
        Assert.Equal(
            [
                OptimizationActionIds.VerifyFiveMIsStopped,
                OptimizationActionIds.VerifyGtaVIsStopped,
                OptimizationActionIds.DiagnoseBottleneck,
                OptimizationActionIds.DetectOverlaysAndCaptureSoftware,
                OptimizationActionIds.ReadFiveMLegacyLogs,
                OptimizationActionIds.GuidePerformanceDiagnostics,
                OptimizationActionIds.DiagnoseNetworkHealth,
                OptimizationActionIds.DiagnoseThermalThrottling,
                OptimizationActionIds.DiagnosePagefileCommit,
                OptimizationActionIds.DiagnoseCacheIntegrity,
                OptimizationActionIds.DetectGpuVendor,
                OptimizationActionIds.CleanUserTemporaryFiles,
                OptimizationActionIds.PruneLegacyCrashDumps,
                OptimizationActionIds.EnableGameMode,
                OptimizationActionIds.PreferHighPerformanceGpu,
                OptimizationActionIds.ApplyLightLegacyGraphics,
                OptimizationActionIds.ApplyLightGtaVGraphics
            ],
            Ids(plan));
        Assert.All(plan.Actions, action =>
            Assert.Equal(RequiredPrivilege.StandardUser, action.Metadata.RequiredPrivilege));
    }

    [Fact]
    public void BalancedProfile_AddsSessionPowerCaptureAndBalancedGraphics()
    {
        var plan = Build(OptimizationProfile.Balanced);

        Assert.True(plan.IsExecutable);
        Assert.True(plan.RequiresElevation);
        Assert.Equal(ActionRisk.Moderate, plan.MaximumRisk);
        Assert.Contains(OptimizationActionIds.DisableBackgroundCapture, Ids(plan));
        Assert.Contains(OptimizationActionIds.EnableSessionPerformancePowerPlan, Ids(plan));
        Assert.Contains(OptimizationActionIds.ApplyBalancedLegacyGraphics, Ids(plan));
        Assert.Contains(OptimizationActionIds.ApplyBalancedGtaVGraphics, Ids(plan));
        Assert.DoesNotContain(OptimizationActionIds.ApplyAggressiveLegacyGraphics, Ids(plan));
        Assert.DoesNotContain(OptimizationActionIds.ApplyAggressiveGtaVGraphics, Ids(plan));
        Assert.DoesNotContain(OptimizationActionIds.ReduceWindowsVisualEffects, Ids(plan));
    }

    [Fact]
    public void AggressiveProfile_UsesAggressiveGraphicsAndReducedVisualEffects()
    {
        var plan = Build(OptimizationProfile.Aggressive);

        Assert.True(plan.IsExecutable);
        Assert.True(plan.RequiresElevation);
        Assert.Equal(ActionRisk.High, plan.MaximumRisk);
        Assert.Contains(OptimizationActionIds.ApplyAggressiveLegacyGraphics, Ids(plan));
        Assert.Contains(OptimizationActionIds.ApplyAggressiveGtaVGraphics, Ids(plan));
        Assert.Contains(OptimizationActionIds.ReduceWindowsVisualEffects, Ids(plan));
        Assert.DoesNotContain(OptimizationActionIds.ApplyBalancedLegacyGraphics, Ids(plan));
        Assert.DoesNotContain(OptimizationActionIds.ApplyBalancedGtaVGraphics, Ids(plan));
        Assert.Contains(plan.Notices, notice => notice.Code == "aggressive-prioritizes-performance");
    }

    [Fact]
    public void GtaVGraphics_AreOffByDefaultUntilInstallationIsConfirmed()
    {
        var plan = Build(OptimizationProfile.Balanced, new OptimizationOptionsDto());

        Assert.DoesNotContain(
            OptimizationActionIds.ApplyBalancedGtaVGraphics,
            Ids(plan));
        Assert.Contains(OptimizationActionIds.ApplyBalancedLegacyGraphics, Ids(plan));
    }

    [Theory]
    [InlineData(CacheRepairPolicy.WhenOversized)]
    [InlineData(CacheRepairPolicy.RepairNow)]
    public void CacheRepair_IsExplicitOptInAndCarriesRebuildWarning(CacheRepairPolicy policy)
    {
        var defaultPlan = Build(OptimizationProfile.Balanced);
        var repairPlan = Build(
            OptimizationProfile.Balanced,
            new OptimizationOptionsDto { ServerCacheRepair = policy });

        Assert.DoesNotContain(OptimizationActionIds.RepairLegacyServerCache, Ids(defaultPlan));
        Assert.Contains(OptimizationActionIds.RepairLegacyServerCache, Ids(repairPlan));
        Assert.Contains(repairPlan.Notices, notice =>
            notice.Code == "server-cache-will-be-rebuilt" &&
            notice.ActionId == OptimizationActionIds.RepairLegacyServerCache);
    }

    [Fact]
    public void DisabledOptions_RemoveTheirActionsButKeepSafetyPreflight()
    {
        var options = new OptimizationOptionsDto
        {
            CleanUserTemporaryFiles = false,
            RemoveOldFiveMCrashDumps = false,
            EnableGameMode = false,
            PreferHighPerformanceGpu = false,
            DisableBackgroundCapture = false,
            UseSessionPerformancePowerPlan = false,
            ApplyLegacyGraphicsPreset = false,
            ApplyGtaVGraphicsPreset = false,
            ReduceWindowsVisualEffects = false
        };

        var plan = Build(OptimizationProfile.Aggressive, options);

        Assert.True(plan.IsExecutable);
        // Segurança e diagnósticos somente leitura sempre permanecem (ActionOptionGate.Always);
        // não são tweaks desativáveis, pois não alteram nada e não têm custo.
        Assert.Equal(
            [
                OptimizationActionIds.VerifyFiveMIsStopped,
                OptimizationActionIds.DiagnoseBottleneck,
                OptimizationActionIds.DetectOverlaysAndCaptureSoftware,
                OptimizationActionIds.ReadFiveMLegacyLogs,
                OptimizationActionIds.GuidePerformanceDiagnostics,
                OptimizationActionIds.DiagnoseNetworkHealth,
                OptimizationActionIds.DiagnoseThermalThrottling,
                OptimizationActionIds.DiagnosePagefileCommit,
                OptimizationActionIds.DiagnoseCacheIntegrity,
                OptimizationActionIds.DetectGpuVendor
            ],
            Ids(plan));
        Assert.False(plan.RequiresElevation);
        Assert.False(plan.ContainsNonReversibleActions);
        Assert.Equal(ActionRisk.Informational, plan.MaximumRisk);
    }

    [Fact]
    public void EnhancedEdition_IsBlockedWithoutLegacyActions()
    {
        var plan = new PlanBuilder().Build(new OptimizationPlanRequestDto
        {
            Profile = OptimizationProfile.Aggressive,
            Edition = FiveMEdition.Enhanced
        });

        Assert.False(plan.IsExecutable);
        Assert.Empty(plan.Actions);
        var block = Assert.Single(plan.Blocks);
        Assert.Equal(PlanBlockCode.EnhancedNotSupported, block.Code);
        Assert.Contains("Enhanced", block.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownEdition_IsBlocked()
    {
        var plan = new PlanBuilder().Build(new OptimizationPlanRequestDto
        {
            Profile = OptimizationProfile.Light,
            Edition = FiveMEdition.Unknown
        });

        Assert.False(plan.IsExecutable);
        Assert.Empty(plan.Actions);
        Assert.Equal(PlanBlockCode.EditionNotDetected, Assert.Single(plan.Blocks).Code);
    }

    [Theory]
    [InlineData(0, 14, 8)]
    [InlineData(31, 14, 8)]
    [InlineData(7, 0, 8)]
    [InlineData(7, 366, 8)]
    [InlineData(7, 14, 0)]
    [InlineData(7, 14, 257)]
    public void UnsafeNumericOptions_AreRejected(int tempAge, int retention, int cacheThreshold)
    {
        var request = new OptimizationPlanRequestDto
        {
            Profile = OptimizationProfile.Balanced,
            Edition = FiveMEdition.Legacy,
            Options = new OptimizationOptionsDto
            {
                TemporaryFileMinimumAgeDays = tempAge,
                DiagnosticRetentionDays = retention,
                ServerCacheThresholdGiB = cacheThreshold
            }
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanBuilder().Build(request));
    }

    [Fact]
    public void EveryPlannedAction_ComesFromTheVersionedCatalog()
    {
        foreach (var profile in Enum.GetValues<OptimizationProfile>())
        {
            var plan = Build(
                profile,
                new OptimizationOptionsDto { ServerCacheRepair = CacheRepairPolicy.RepairNow });

            Assert.All(plan.Actions, action =>
            {
                var definition = ActionCatalog.Current.GetRequired(action.Metadata.Id);
                Assert.Equal(definition.Version, action.Metadata.Version);
                Assert.Contains(profile, definition.SupportedProfiles);
            });
        }
    }

    [Fact]
    public void PlanCarriesProductIdentityAndVersionInformation()
    {
        var plan = Build(OptimizationProfile.Balanced);

        Assert.Equal("FiveMCleaner", plan.ProductName);
        Assert.Equal("optimizer for FiveM", plan.ProductSubtitle);
        Assert.Equal(ProductIdentity.PlanSchemaVersion, plan.SchemaVersion);
        Assert.Equal(ActionCatalog.CurrentVersion, plan.CatalogVersion);
        Assert.NotEqual(Guid.Empty, plan.PlanId);
    }

    private static OptimizationPlanDto Build(
        OptimizationProfile profile,
        OptimizationOptionsDto? options = null)
    {
        return new PlanBuilder().Build(new OptimizationPlanRequestDto
        {
            Profile = profile,
            Edition = FiveMEdition.Legacy,
            Options = options ?? new OptimizationOptionsDto { ApplyGtaVGraphicsPreset = true }
        });
    }

    private static string[] Ids(OptimizationPlanDto plan)
    {
        return plan.Actions.Select(action => action.Metadata.Id).ToArray();
    }
}
