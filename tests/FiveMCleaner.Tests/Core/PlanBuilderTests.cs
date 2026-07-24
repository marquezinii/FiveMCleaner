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
                OptimizationActionIds.DiagnoseCpuDetails,
                OptimizationActionIds.DiagnoseGpuDetails,
                OptimizationActionIds.DiagnoseRamDetails,
                OptimizationActionIds.DiagnoseStorageHealth,
                OptimizationActionIds.DiagnoseDriverVersions,
                OptimizationActionIds.DiagnoseDisplayConfiguration,
                OptimizationActionIds.DiagnoseSessionSettings,
                OptimizationActionIds.DiagnoseThrottlingSignal,
                OptimizationActionIds.DiagnoseResourceUsage,
                OptimizationActionIds.DiagnosePciLink,
                OptimizationActionIds.DiagnoseHardwareStability,
                OptimizationActionIds.ClassifyBottleneck,
                OptimizationActionIds.DiagnoseCacheStorage,
                OptimizationActionIds.DiagnoseInstallationHealth,
                OptimizationActionIds.DiagnoseCrashPatterns,
                OptimizationActionIds.RecommendGraphicsPreset,
                OptimizationActionIds.DiagnoseTextureVramFit,
                OptimizationActionIds.DiagnoseGtaVLaunchParameters,
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
    public void RepairActions_AreOptInAndNeverPartOfAnyDefaultProfile()
    {
        var light = Build(OptimizationProfile.Light);
        var balanced = Build(OptimizationProfile.Balanced);
        var aggressive = Build(OptimizationProfile.Aggressive);

        foreach (var plan in new[] { light, balanced, aggressive })
        {
            Assert.DoesNotContain(OptimizationActionIds.TerminateStuckFiveMProcess, Ids(plan));
            Assert.DoesNotContain(OptimizationActionIds.RecreateFiveMLocalData, Ids(plan));
            Assert.DoesNotContain(OptimizationActionIds.RepairStaleAuthData, Ids(plan));
        }

        var repairPlan = Build(
            OptimizationProfile.Aggressive,
            new OptimizationOptionsDto
            {
                TerminateStuckFiveMProcess = true,
                RecreateFiveMLocalData = true,
                RepairStaleAuthData = true
            });

        Assert.Contains(OptimizationActionIds.TerminateStuckFiveMProcess, Ids(repairPlan));
        Assert.Contains(OptimizationActionIds.RecreateFiveMLocalData, Ids(repairPlan));
        Assert.Contains(OptimizationActionIds.RepairStaleAuthData, Ids(repairPlan));
        Assert.Contains(repairPlan.Notices, notice =>
            notice.Code == "stuck-process-termination-loses-unsaved-state");
        Assert.Contains(repairPlan.Notices, notice =>
            notice.Code == "local-data-recreation-is-a-repair-not-daily-optimization");
        Assert.Contains(repairPlan.Notices, notice =>
            notice.Code == "auth-data-repair-requires-detected-error-pattern");
    }

    [Fact]
    public void GraphicsPresetsAndDisplayPreferences_AreOptInAndNeverPartOfAnyDefaultProfile()
    {
        var light = Build(OptimizationProfile.Light);
        var balanced = Build(OptimizationProfile.Balanced);
        var aggressive = Build(OptimizationProfile.Aggressive);

        foreach (var plan in new[] { light, balanced, aggressive })
        {
            Assert.DoesNotContain(OptimizationActionIds.ApplyQualityLegacyGraphics, Ids(plan));
            Assert.DoesNotContain(OptimizationActionIds.ApplyQualityGtaVGraphics, Ids(plan));
            Assert.DoesNotContain(OptimizationActionIds.ApplyLegacyDisplayPreferences, Ids(plan));
            Assert.DoesNotContain(OptimizationActionIds.ApplyGtaVDisplayPreferences, Ids(plan));
        }

        var enabledPlan = Build(
            OptimizationProfile.Aggressive,
            new OptimizationOptionsDto
            {
                ApplyQualityGraphicsPreset = true,
                ApplyDisplayPreferences = true
            });

        Assert.Contains(OptimizationActionIds.ApplyQualityLegacyGraphics, Ids(enabledPlan));
        Assert.Contains(OptimizationActionIds.ApplyQualityGtaVGraphics, Ids(enabledPlan));
        Assert.Contains(OptimizationActionIds.ApplyLegacyDisplayPreferences, Ids(enabledPlan));
        Assert.Contains(OptimizationActionIds.ApplyGtaVDisplayPreferences, Ids(enabledPlan));
        Assert.Contains(enabledPlan.Notices, notice => notice.Code == "quality-preset-may-reduce-fps");
        Assert.Contains(enabledPlan.Notices, notice => notice.Code == "display-preferences-do-not-change-resolution");
    }

    [Fact]
    public void GtaVLaunchParameters_AreOptInAndNeverPartOfAnyDefaultProfile()
    {
        var light = Build(OptimizationProfile.Light);
        var balanced = Build(OptimizationProfile.Balanced);
        var aggressive = Build(OptimizationProfile.Aggressive);

        foreach (var plan in new[] { light, balanced, aggressive })
        {
            Assert.DoesNotContain(OptimizationActionIds.ApplyGtaVGraphicsLaunchParameters, Ids(plan));
            Assert.DoesNotContain(OptimizationActionIds.ApplyGtaVDisplayLaunchParameters, Ids(plan));
            Assert.DoesNotContain(OptimizationActionIds.ApplyGtaVRepairLaunchParameters, Ids(plan));
        }

        var enabledPlan = Build(
            OptimizationProfile.Aggressive,
            new OptimizationOptionsDto
            {
                ApplyGtaVGraphicsLaunchParameters = true,
                ApplyGtaVDisplayLaunchParameters = true,
                ApplyGtaVRepairLaunchParameters = true,
                UseGtaVSafeMode = true
            });

        Assert.Contains(OptimizationActionIds.ApplyGtaVGraphicsLaunchParameters, Ids(enabledPlan));
        Assert.Contains(OptimizationActionIds.ApplyGtaVDisplayLaunchParameters, Ids(enabledPlan));
        Assert.Contains(OptimizationActionIds.ApplyGtaVRepairLaunchParameters, Ids(enabledPlan));
        Assert.Contains(enabledPlan.Notices, notice => notice.Code == "gtav-repair-launch-parameters-are-temporary");
        Assert.Contains(enabledPlan.Notices, notice => notice.Code == "gtav-launch-parameters-do-not-affect-fivem");
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
                OptimizationActionIds.DetectGpuVendor,
                OptimizationActionIds.DiagnoseCpuDetails,
                OptimizationActionIds.DiagnoseGpuDetails,
                OptimizationActionIds.DiagnoseRamDetails,
                OptimizationActionIds.DiagnoseStorageHealth,
                OptimizationActionIds.DiagnoseDriverVersions,
                OptimizationActionIds.DiagnoseDisplayConfiguration,
                OptimizationActionIds.DiagnoseSessionSettings,
                OptimizationActionIds.DiagnoseThrottlingSignal,
                OptimizationActionIds.DiagnoseResourceUsage,
                OptimizationActionIds.DiagnosePciLink,
                OptimizationActionIds.DiagnoseHardwareStability,
                OptimizationActionIds.ClassifyBottleneck,
                OptimizationActionIds.DiagnoseCacheStorage,
                OptimizationActionIds.DiagnoseInstallationHealth,
                OptimizationActionIds.DiagnoseCrashPatterns,
                OptimizationActionIds.RecommendGraphicsPreset,
                OptimizationActionIds.DiagnoseTextureVramFit,
                OptimizationActionIds.DiagnoseGtaVLaunchParameters
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
