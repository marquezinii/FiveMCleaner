using Ralven.Contracts;
using Ralven.Core.Catalog;
using Ralven.Core.Planning;
using Ralven.Windows;
using Ralven.Windows.Actions;
using Xunit;

namespace Ralven.Tests.Windows;

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
    public void ResolveActions_RejectsPersonalOptionsEvenWhenTheActionListWasNotChanged()
    {
        using var directory = new TemporaryDirectory();
        var (runtime, _, _) = WindowsTestRuntime.Create(directory);
        var plan = PlanBuilder.Build(new OptimizationPlanRequestDto
        {
            Scope = OptimizationScope.GeneralWindows,
            Profile = OptimizationProfile.Aggressive,
            Edition = FiveMEdition.Unknown,
            PersonalPreferences = new()
        }, PlanBuildContext.New(TimeProvider.System));

        Assert.NotEmpty(runtime.ResolveActions(plan));
        Assert.Throws<InvalidOperationException>(() => runtime.ResolveActions(plan with
        {
            Options = plan.Options with { TemporaryFileMinimumAgeDays = 29 }
        }));
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

    [Theory]
    [InlineData(FiveMEdition.Unknown)]
    [InlineData(FiveMEdition.Legacy)]
    [InlineData(FiveMEdition.Enhanced)]
    public void ResolveActions_AcceptsCanonicalGeneralWindowsPlanRegardlessOfFiveMEdition(
        FiveMEdition edition)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var (runtime, _, _) = WindowsTestRuntime.Create(temporaryDirectory);
        var plan = BuildPlan(OptimizationProfile.Balanced, OptimizationScope.GeneralWindows, edition);

        var actions = runtime.ResolveActions(plan);

        Assert.NotEmpty(actions);
        Assert.Equal(
            plan.Actions.Select(action => action.Metadata.Id),
            actions.Select(action => action.Metadata.Id));
    }

    [Fact]
    public void ResolveActions_RejectsTamperedScope()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var (runtime, _, _) = WindowsTestRuntime.Create(temporaryDirectory);
        var plan = BuildPlan(
            OptimizationProfile.Balanced,
            OptimizationScope.GeneralWindows,
            FiveMEdition.Legacy);

        Assert.Throws<InvalidOperationException>(() =>
            runtime.ResolveActions(plan with { Scope = OptimizationScope.FiveMLegacy }));
    }

    [Fact]
    public void ResolveActions_RejectsFiveMLegacyScopeForEnhancedEdition()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var (runtime, _, _) = WindowsTestRuntime.Create(temporaryDirectory);
        var plan = BuildPlan(OptimizationProfile.Balanced);

        Assert.Throws<InvalidOperationException>(() =>
            runtime.ResolveActions(plan with { Edition = FiveMEdition.Enhanced }));
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
            FileTree = new Ralven.Windows.Infrastructure.SafeFileTree(),
            VisualEffects = new FakeVisualEffectsController(),
            PowerPlans = new FakePowerPlanController(),
            PowerStatus = new FakePowerStatusProvider(),
            JournalStore = journals,
            SystemResources = new FakeSystemResourceInspector(),
            ActionText = static (key, _) => key,
            WindowsSystemHealth = new FakeWindowsSystemHealthInspector(),
            ApplicationInventory = new FakeWindowsApplicationInventoryInspector(),
            TrimStatus = new FakeTrimStatusInspector(),
            MouseAcceleration = new FakeMouseAccelerationInspector(),
            OverlaySoftware = new FakeOverlaySoftwareInspector(),
            NetworkHealth = new FakeNetworkHealthInspector(),
            Thermal = new FakeThermalInspector(),
            GpuVendor = new FakeGpuVendorInspector(),
            Cpu = new FakeCpuInspector(),
            GpuDetails = new FakeGpuDetailsInspector(),
            RamDetails = new FakeRamDetailsInspector(),
            StorageHealth = new FakeStorageHealthInspector(),
            DriverVersions = new FakeDriverVersionInspector(),
            DisplayConfiguration = new FakeDisplayConfigurationInspector(),
            ResourceUsage = new FakeResourceUsageInspector(),
            PciLink = new FakePciLinkInspector(),
            HardwareStability = new FakeHardwareStabilityInspector(),
            BackgroundProcess = new FakeBackgroundProcessInspector(),
            StuckProcess = new FakeStuckFiveMProcessInspector(),
            StuckProcessTerminator = new FakeFiveMProcessTerminator(),
            VendorLaptopSoftware = new FakeVendorLaptopSoftwareInspector()
        };

        Assert.Throws<InvalidOperationException>(() =>
            Ralven.Windows.WindowsOptimizationRuntime.Create(invalid, dependencies));
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

    private static OptimizationPlanDto BuildPlan(
        OptimizationProfile profile,
        OptimizationScope scope = OptimizationScope.FiveMLegacy,
        FiveMEdition edition = FiveMEdition.Legacy)
    {
        return PlanBuilder.Build(
            new OptimizationPlanRequestDto
            {
                Scope = scope,
                Profile = profile,
                Edition = edition,
                Options = new OptimizationOptionsDto
                {
                    ServerCacheRepair = CacheRepairPolicy.RepairNow,
                    ApplyGtaVGraphicsPreset = true
                }
            },
            PlanBuildContext.New(TimeProvider.System));
    }
}
