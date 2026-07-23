using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Core.Planning;
using FiveMCleaner.Windows;
using FiveMCleaner.Windows.Actions;
using Xunit;

namespace FiveMCleaner.Tests.Windows;

public sealed class WindowsOptimizationRuntimeTests
{
    [Fact]
    public void Catalog_RegistersEveryCoreActionWithExactMetadata()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var (runtime, _, _) = WindowsTestRuntime.Create(temporaryDirectory);

        Assert.Equal(ActionCatalog.Current.Actions.Count, runtime.Catalog.Actions.Count);
        foreach (var definition in ActionCatalog.Current.Actions)
        {
            var handler = runtime.Catalog.GetRequired(definition.Id, definition.Version);
            var expected = definition.ToMetadata();
            Assert.Equal(expected.Id, handler.Metadata.Id);
            Assert.Equal(expected.Version, handler.Metadata.Version);
            Assert.Equal(expected.RequiredPrivilege, handler.Metadata.RequiredPrivilege);
            Assert.Equal(expected.SupportedProfiles, handler.Metadata.SupportedProfiles);
        }
    }

    [Fact]
    public void ResolveActions_UsesCanonicalPlanOrderAndProfileSpecificGraphics()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var (runtime, _, _) = WindowsTestRuntime.Create(temporaryDirectory);
        var plan = BuildPlan(OptimizationProfile.Balanced);

        var actions = runtime.ResolveActions(plan);

        Assert.Equal(
            plan.Actions.Select(action => action.Metadata.Id),
            actions.Select(action => action.Metadata.Id));
        var graphics = actions.OfType<LegacyGraphicsPresetAction>().ToArray();
        Assert.Collection(
            graphics,
            action => Assert.Equal(
                OptimizationActionIds.ApplyBalancedLegacyGraphics,
                action.Metadata.Id),
            action => Assert.Equal(
                OptimizationActionIds.ApplyBalancedGtaVGraphics,
                action.Metadata.Id));
    }

    [Fact]
    public void ResolveActions_RejectsTamperedMetadata()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var (runtime, _, _) = WindowsTestRuntime.Create(temporaryDirectory);
        var plan = BuildPlan(OptimizationProfile.Balanced);
        var first = plan.Actions[0];
        var tampered = plan with
        {
            Actions =
            [
                first with { Metadata = first.Metadata with { Version = 999 } },
                .. plan.Actions.Skip(1)
            ]
        };

        Assert.Throws<InvalidOperationException>(() => runtime.ResolveActions(tampered));
    }

    [Fact]
    public void AdministratorResolver_ReturnsOnlyCoreAdministratorActions()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var (runtime, _, _) = WindowsTestRuntime.Create(temporaryDirectory);
        var plan = BuildPlan(OptimizationProfile.Aggressive);

        var actions = runtime.ResolveAdministratorActions(plan);

        var action = Assert.Single(actions);
        Assert.Equal(OptimizationActionIds.EnableSessionPerformancePowerPlan, action.Metadata.Id);
        Assert.Equal(RequiredPrivilege.Administrator, action.Metadata.RequiredPrivilege);
        Assert.Throws<UnauthorizedAccessException>(() =>
            runtime.ResolveAdministratorActions(
            [
                (OptimizationActionIds.EnableGameMode,
                    ActionCatalog.Current.GetRequired(OptimizationActionIds.EnableGameMode).Version)
            ]));
    }

    [Fact]
    public void Environment_RejectsExecutableOutsideInstallationRoot()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var (_, environment, journals) = WindowsTestRuntime.Create(temporaryDirectory);
        var invalid = environment with
        {
            FiveMExecutablePath = temporaryDirectory.Combine("outside.exe")
        };
        var dependencies = new WindowsOptimizationDependencies
        {
            Registry = new FakeRegistryStore(),
            ProcessInspector = new FakeProcessInspector(),
            GtaVProcessInspector = new FakeGtaVProcessInspector(),
            FileTree = new FiveMCleaner.Windows.Infrastructure.SafeFileTree(),
            VisualEffects = new FakeVisualEffectsController(),
            PowerPlans = new FakePowerPlanController(),
            PowerStatus = new FakePowerStatusProvider(),
            JournalStore = journals,
            SystemResources = new FakeSystemResourceInspector(),
            OverlaySoftware = new FakeOverlaySoftwareInspector(),
            NetworkHealth = new FakeNetworkHealthInspector(),
            Thermal = new FakeThermalInspector(),
            GpuVendor = new FakeGpuVendorInspector()
        };

        Assert.Throws<InvalidOperationException>(() =>
            FiveMCleaner.Windows.WindowsOptimizationRuntime.Create(invalid, dependencies));
    }

    [Fact]
    public void DetectDefault_UsesTheWindowsProfileTempInsteadOfEnvironmentOverrides()
    {
        var environment = WindowsOptimizationEnvironment.DetectDefault();
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        Assert.Equal(
            Path.Combine(localAppData, "Temp"),
            environment.UserTemporaryDirectory,
            ignoreCase: true);
    }

    private static OptimizationPlanDto BuildPlan(OptimizationProfile profile)
    {
        return new PlanBuilder().Build(new OptimizationPlanRequestDto
        {
            Profile = profile,
            Edition = FiveMEdition.Legacy,
            Options = new OptimizationOptionsDto
            {
                ServerCacheRepair = CacheRepairPolicy.RepairNow,
                ApplyGtaVGraphicsPreset = true
            }
        });
    }
}
