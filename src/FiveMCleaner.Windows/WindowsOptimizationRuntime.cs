using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Core.Planning;
using FiveMCleaner.Windows.Actions;
using FiveMCleaner.Windows.Engine;
using FiveMCleaner.Windows.Infrastructure;

namespace FiveMCleaner.Windows;

public sealed record WindowsOptimizationEnvironment
{
    public required string FiveMInstallationRoot { get; init; }

    public required string FiveMAppRoot { get; init; }

    public required string FiveMExecutablePath { get; init; }

    public required string LegacyGraphicsSettingsPath { get; init; }

    public string? GtaVInstallationRoot { get; init; }

    public string? GtaVExecutablePath { get; init; }

    public required string GtaVGraphicsSettingsPath { get; init; }

    public required string UserTemporaryDirectory { get; init; }

    public required string JournalDirectory { get; init; }

    public static WindowsOptimizationEnvironment DetectDefault()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var roamingAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData) || string.IsNullOrWhiteSpace(roamingAppData))
        {
            throw new InvalidOperationException("Windows user profile directories are unavailable.");
        }

        var installationRoot = Path.Combine(localAppData, "FiveM");
        var gtaV = GtaVLocator.Detect(installationRoot);
        return new WindowsOptimizationEnvironment
        {
            FiveMInstallationRoot = installationRoot,
            FiveMAppRoot = Path.Combine(installationRoot, "FiveM.app"),
            FiveMExecutablePath = Path.Combine(installationRoot, "FiveM.exe"),
            LegacyGraphicsSettingsPath = Path.Combine(
                roamingAppData,
                "CitizenFX",
                "gta5_settings.xml"),
            GtaVInstallationRoot = gtaV.InstallationRoot,
            GtaVExecutablePath = gtaV.ExecutablePath,
            GtaVGraphicsSettingsPath = gtaV.GraphicsSettingsPath,
            UserTemporaryDirectory = Path.Combine(localAppData, "Temp"),
            JournalDirectory = Path.Combine(localAppData, "FiveMCleaner", "Transactions")
        };
    }
}

public sealed record WindowsOptimizationDependencies
{
    public required IRegistryStore Registry { get; init; }

    public required IFiveMProcessInspector ProcessInspector { get; init; }

    public required IGtaVProcessInspector GtaVProcessInspector { get; init; }

    public required SafeFileTree FileTree { get; init; }

    public required IVisualEffectsController VisualEffects { get; init; }

    public required IPowerPlanController PowerPlans { get; init; }

    public required IPowerStatusProvider PowerStatus { get; init; }

    public required IWindowsTransactionJournalStore JournalStore { get; init; }

    public required ISystemResourceInspector SystemResources { get; init; }

    public required IOverlaySoftwareInspector OverlaySoftware { get; init; }

    public required INetworkHealthInspector NetworkHealth { get; init; }

    public required IThermalInspector Thermal { get; init; }

    public required IGpuVendorInspector GpuVendor { get; init; }

    public required ICpuInspector Cpu { get; init; }

    public required IGpuDetailsInspector GpuDetails { get; init; }

    public required IRamDetailsInspector RamDetails { get; init; }

    public required IStorageHealthInspector StorageHealth { get; init; }

    public required IDriverVersionInspector DriverVersions { get; init; }

    public required IDisplayConfigurationInspector DisplayConfiguration { get; init; }

    public required IResourceUsageInspector ResourceUsage { get; init; }

    public required IPciLinkInspector PciLink { get; init; }

    public required IHardwareStabilityInspector HardwareStability { get; init; }

    public required IBackgroundProcessInspector BackgroundProcess { get; init; }

    public required IStuckFiveMProcessInspector StuckProcess { get; init; }

    public static WindowsOptimizationDependencies CreateDefault(
        WindowsOptimizationEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var commandRunner = new ProcessCommandRunner();
        return new WindowsOptimizationDependencies
        {
            Registry = new WindowsRegistryStore(),
            ProcessInspector = new WindowsFiveMProcessInspector(),
            GtaVProcessInspector = new WindowsGtaVProcessInspector(),
            FileTree = new SafeFileTree(),
            VisualEffects = new WindowsVisualEffectsController(),
            PowerPlans = new PowerCfgController(commandRunner),
            PowerStatus = new WindowsPowerStatusProvider(),
            JournalStore = new JsonWindowsTransactionJournalStore(environment.JournalDirectory),
            SystemResources = new WindowsSystemResourceInspector(),
            OverlaySoftware = new WindowsOverlaySoftwareInspector(),
            NetworkHealth = new WindowsNetworkHealthInspector(),
            Thermal = new WindowsThermalInspector(),
            GpuVendor = new WindowsGpuVendorInspector(),
            Cpu = new WindowsCpuInspector(),
            GpuDetails = new WindowsGpuDetailsInspector(),
            RamDetails = new WindowsRamDetailsInspector(),
            StorageHealth = new WindowsStorageHealthInspector(),
            DriverVersions = new WindowsDriverVersionInspector(),
            DisplayConfiguration = new WindowsDisplayConfigurationInspector(),
            ResourceUsage = new WindowsResourceUsageInspector(),
            PciLink = new WindowsPciLinkInspector(),
            HardwareStability = new WindowsHardwareStabilityInspector(),
            BackgroundProcess = new WindowsBackgroundProcessInspector(),
            StuckProcess = new WindowsStuckFiveMProcessInspector()
        };
    }
}

public sealed class WindowsOptimizationActionFactory
{
    private const long GiB = 1024L * 1024L * 1024L;
    private readonly WindowsOptimizationEnvironment environment;
    private readonly WindowsOptimizationDependencies dependencies;

    public WindowsOptimizationActionFactory(
        WindowsOptimizationEnvironment environment,
        WindowsOptimizationDependencies dependencies)
    {
        this.environment = ValidateEnvironment(environment);
        this.dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
    }

    private string RosIdPath =>
        Path.Combine(Path.GetDirectoryName(environment.LegacyGraphicsSettingsPath)!, "ros_id.dat");

    private string DigitalEntitlementsRoot =>
        Path.Combine(Path.GetDirectoryName(environment.UserTemporaryDirectory)!, "DigitalEntitlements");

    private string AuthQuarantineRoot =>
        Path.Combine(Path.GetDirectoryName(environment.JournalDirectory)!, "AuthQuarantine");

    public IReadOnlyList<IWindowsOptimizationAction> Create(OptimizationPlanDto plan)
    {
        ValidatePlan(plan);
        return plan.Actions
            .OrderBy(action => action.Sequence)
            .Select(action => CreateAction(action.Metadata.Id, plan))
            .ToArray();
    }

    internal IReadOnlyList<IWindowsOptimizationAction> CreateCatalogActions()
    {
        var defaults = new OptimizationOptionsDto();
        return
        [
            new VerifyFiveMStoppedAction(
                environment.FiveMInstallationRoot,
                dependencies.ProcessInspector),
            new VerifyGtaVStoppedAction(
                environment.GtaVInstallationRoot,
                dependencies.GtaVProcessInspector),
            new BottleneckDiagnosisAction(dependencies.SystemResources),
            new OverlaySoftwareDetectionAction(dependencies.OverlaySoftware),
            new FiveMLegacyLogReaderAction(environment.FiveMAppRoot),
            new PerformanceDiagnosticsGuideAction(),
            new NetworkHealthDiagnosisAction(dependencies.NetworkHealth),
            new ThermalDiagnosisAction(dependencies.Thermal),
            new PagefileCommitDiagnosisAction(dependencies.SystemResources),
            new CacheIndexIntegrityDiagnosisAction(environment.FiveMAppRoot),
            new GpuVendorDetectionAction(dependencies.GpuVendor),
            new CpuDetailsDiagnosisAction(dependencies.Cpu),
            new GpuDetailsDiagnosisAction(dependencies.GpuDetails),
            new RamDetailsDiagnosisAction(dependencies.RamDetails),
            new StorageHealthDiagnosisAction(dependencies.StorageHealth),
            new DriverVersionsDiagnosisAction(dependencies.DriverVersions),
            new DisplayConfigurationDiagnosisAction(dependencies.DisplayConfiguration),
            new SessionSettingsDiagnosisAction(dependencies.Registry, dependencies.PowerPlans),
            new ThrottlingSignalDiagnosisAction(
                dependencies.Cpu,
                dependencies.ResourceUsage,
                dependencies.HardwareStability,
                dependencies.Thermal),
            new ResourceUsageDiagnosisAction(dependencies.ResourceUsage),
            new PciLinkDiagnosisAction(dependencies.PciLink),
            new HardwareStabilityDiagnosisAction(dependencies.HardwareStability),
            new BottleneckClassificationAction(
                dependencies.SystemResources,
                dependencies.ResourceUsage,
                dependencies.Thermal,
                dependencies.NetworkHealth,
                dependencies.GpuDetails,
                dependencies.BackgroundProcess),
            new GtaVLaunchParametersDiagnosisAction(environment.GtaVInstallationRoot),
            new GraphicsPresetRecommendationAction(
                dependencies.GpuDetails,
                dependencies.Cpu,
                dependencies.RamDetails,
                dependencies.DisplayConfiguration),
            new TextureVramFitDiagnosisAction(
                environment.LegacyGraphicsSettingsPath,
                dependencies.GpuDetails),
            new CacheStorageDiagnosisAction(environment.FiveMAppRoot),
            new InstallationHealthDiagnosisAction(
                environment.FiveMInstallationRoot,
                environment.FiveMAppRoot),
            new CrashPatternDiagnosisAction(environment.FiveMAppRoot),
            new UserTemporaryFilesCleanupAction(
                environment.UserTemporaryDirectory,
                TimeSpan.FromDays(defaults.TemporaryFileMinimumAgeDays),
                dependencies.FileTree),
            new LegacyCrashDumpsPruneAction(
                environment.FiveMAppRoot,
                environment.FiveMInstallationRoot,
                TimeSpan.FromDays(defaults.DiagnosticRetentionDays),
                dependencies.ProcessInspector,
                dependencies.FileTree),
            new LegacyServerCacheRepairAction(
                environment.FiveMAppRoot,
                environment.FiveMInstallationRoot,
                CacheRepairPolicy.RepairNow,
                checked(defaults.ServerCacheThresholdGiB * GiB),
                dependencies.ProcessInspector,
                dependencies.FileTree),
            new StuckProcessTerminationAction(
                environment.FiveMInstallationRoot,
                dependencies.StuckProcess),
            new RecreateFiveMLocalDataAction(
                environment.FiveMAppRoot,
                environment.FiveMInstallationRoot,
                dependencies.ProcessInspector,
                dependencies.FileTree),
            new StaleAuthDataRepairAction(
                environment.FiveMAppRoot,
                environment.FiveMInstallationRoot,
                RosIdPath,
                DigitalEntitlementsRoot,
                AuthQuarantineRoot,
                dependencies.ProcessInspector),
            new GameModeRegistryAction(dependencies.Registry),
            new GpuPreferenceRegistryAction(
                dependencies.Registry,
                environment.FiveMExecutablePath,
                environment.FiveMInstallationRoot),
            new GameDvrRegistryAction(dependencies.Registry),
            new SessionPerformancePowerPlanAction(
                dependencies.PowerPlans,
                dependencies.PowerStatus),
            new LegacyGraphicsPresetAction(
                environment.LegacyGraphicsSettingsPath,
                environment.FiveMInstallationRoot,
                OptimizationProfile.Light,
                GraphicsSettingsTarget.FiveM,
                dependencies.ProcessInspector,
                dependencies.GtaVProcessInspector),
            new LegacyGraphicsPresetAction(
                environment.LegacyGraphicsSettingsPath,
                environment.FiveMInstallationRoot,
                OptimizationProfile.Balanced,
                GraphicsSettingsTarget.FiveM,
                dependencies.ProcessInspector,
                dependencies.GtaVProcessInspector),
            new LegacyGraphicsPresetAction(
                environment.LegacyGraphicsSettingsPath,
                environment.FiveMInstallationRoot,
                OptimizationProfile.Aggressive,
                GraphicsSettingsTarget.FiveM,
                dependencies.ProcessInspector,
                dependencies.GtaVProcessInspector),
            new LegacyGraphicsPresetAction(
                environment.GtaVGraphicsSettingsPath,
                environment.GtaVInstallationRoot,
                OptimizationProfile.Light,
                GraphicsSettingsTarget.GtaV,
                dependencies.ProcessInspector,
                dependencies.GtaVProcessInspector),
            new LegacyGraphicsPresetAction(
                environment.GtaVGraphicsSettingsPath,
                environment.GtaVInstallationRoot,
                OptimizationProfile.Balanced,
                GraphicsSettingsTarget.GtaV,
                dependencies.ProcessInspector,
                dependencies.GtaVProcessInspector),
            new LegacyGraphicsPresetAction(
                environment.GtaVGraphicsSettingsPath,
                environment.GtaVInstallationRoot,
                OptimizationProfile.Aggressive,
                GraphicsSettingsTarget.GtaV,
                dependencies.ProcessInspector,
                dependencies.GtaVProcessInspector),
            new LegacyGraphicsPresetAction(
                environment.LegacyGraphicsSettingsPath,
                environment.FiveMInstallationRoot,
                GraphicsSettingsTarget.FiveM,
                dependencies.ProcessInspector,
                dependencies.GtaVProcessInspector,
                OptimizationActionIds.ApplyQualityLegacyGraphics,
                LegacyGraphicsPresets.Quality,
                GraphicsPresetDirection.RaiseOnly),
            new LegacyGraphicsPresetAction(
                environment.GtaVGraphicsSettingsPath,
                environment.GtaVInstallationRoot,
                GraphicsSettingsTarget.GtaV,
                dependencies.ProcessInspector,
                dependencies.GtaVProcessInspector,
                OptimizationActionIds.ApplyQualityGtaVGraphics,
                LegacyGraphicsPresets.Quality,
                GraphicsPresetDirection.RaiseOnly),
            new DisplayPreferencesAction(
                environment.LegacyGraphicsSettingsPath,
                environment.FiveMInstallationRoot,
                GraphicsSettingsTarget.FiveM,
                defaults.PreferWindowedMode,
                defaults.EnableVSync,
                dependencies.ProcessInspector,
                dependencies.GtaVProcessInspector),
            new DisplayPreferencesAction(
                environment.GtaVGraphicsSettingsPath,
                environment.GtaVInstallationRoot,
                GraphicsSettingsTarget.GtaV,
                defaults.PreferWindowedMode,
                defaults.EnableVSync,
                dependencies.ProcessInspector,
                dependencies.GtaVProcessInspector),
            new GtaVGraphicsLaunchParametersAction(
                environment.GtaVInstallationRoot,
                dependencies.DisplayConfiguration,
                dependencies.GtaVProcessInspector),
            new GtaVDisplayLaunchParametersAction(
                environment.GtaVInstallationRoot,
                defaults.PreferWindowedMode,
                defaults.PreferBorderlessWindow,
                defaults.GtaVLaunchDirectXVersion,
                dependencies.GtaVProcessInspector),
            new GtaVRepairLaunchParametersAction(
                environment.GtaVInstallationRoot,
                defaults.UseGtaVSafeMode,
                defaults.UseGtaVMinimumSettings,
                defaults.UseGtaVAutoSettingsRebuild,
                dependencies.GtaVProcessInspector),
            new VisualEffectsAction(dependencies.VisualEffects)
        ];
    }

    private IWindowsOptimizationAction CreateAction(
        string actionId,
        OptimizationPlanDto plan)
    {
        return actionId switch
        {
            OptimizationActionIds.VerifyFiveMIsStopped => new VerifyFiveMStoppedAction(
                environment.FiveMInstallationRoot,
                dependencies.ProcessInspector),
            OptimizationActionIds.VerifyGtaVIsStopped => new VerifyGtaVStoppedAction(
                environment.GtaVInstallationRoot,
                dependencies.GtaVProcessInspector),
            OptimizationActionIds.DiagnoseBottleneck => new BottleneckDiagnosisAction(
                dependencies.SystemResources),
            OptimizationActionIds.DetectOverlaysAndCaptureSoftware => new OverlaySoftwareDetectionAction(
                dependencies.OverlaySoftware),
            OptimizationActionIds.ReadFiveMLegacyLogs => new FiveMLegacyLogReaderAction(
                environment.FiveMAppRoot),
            OptimizationActionIds.GuidePerformanceDiagnostics => new PerformanceDiagnosticsGuideAction(),
            OptimizationActionIds.DiagnoseNetworkHealth => new NetworkHealthDiagnosisAction(
                dependencies.NetworkHealth),
            OptimizationActionIds.DiagnoseThermalThrottling => new ThermalDiagnosisAction(
                dependencies.Thermal),
            OptimizationActionIds.DiagnosePagefileCommit => new PagefileCommitDiagnosisAction(
                dependencies.SystemResources),
            OptimizationActionIds.DiagnoseCacheIntegrity => new CacheIndexIntegrityDiagnosisAction(
                environment.FiveMAppRoot),
            OptimizationActionIds.DetectGpuVendor => new GpuVendorDetectionAction(
                dependencies.GpuVendor),
            OptimizationActionIds.DiagnoseCpuDetails => new CpuDetailsDiagnosisAction(
                dependencies.Cpu),
            OptimizationActionIds.DiagnoseGpuDetails => new GpuDetailsDiagnosisAction(
                dependencies.GpuDetails),
            OptimizationActionIds.DiagnoseRamDetails => new RamDetailsDiagnosisAction(
                dependencies.RamDetails),
            OptimizationActionIds.DiagnoseStorageHealth => new StorageHealthDiagnosisAction(
                dependencies.StorageHealth),
            OptimizationActionIds.DiagnoseDriverVersions => new DriverVersionsDiagnosisAction(
                dependencies.DriverVersions),
            OptimizationActionIds.DiagnoseDisplayConfiguration => new DisplayConfigurationDiagnosisAction(
                dependencies.DisplayConfiguration),
            OptimizationActionIds.DiagnoseSessionSettings => new SessionSettingsDiagnosisAction(
                dependencies.Registry,
                dependencies.PowerPlans),
            OptimizationActionIds.DiagnoseThrottlingSignal => new ThrottlingSignalDiagnosisAction(
                dependencies.Cpu,
                dependencies.ResourceUsage,
                dependencies.HardwareStability,
                dependencies.Thermal),
            OptimizationActionIds.DiagnoseResourceUsage => new ResourceUsageDiagnosisAction(
                dependencies.ResourceUsage),
            OptimizationActionIds.DiagnosePciLink => new PciLinkDiagnosisAction(
                dependencies.PciLink),
            OptimizationActionIds.DiagnoseHardwareStability => new HardwareStabilityDiagnosisAction(
                dependencies.HardwareStability),
            OptimizationActionIds.ClassifyBottleneck => new BottleneckClassificationAction(
                dependencies.SystemResources,
                dependencies.ResourceUsage,
                dependencies.Thermal,
                dependencies.NetworkHealth,
                dependencies.GpuDetails,
                dependencies.BackgroundProcess),
            OptimizationActionIds.DiagnoseGtaVLaunchParameters => new GtaVLaunchParametersDiagnosisAction(
                environment.GtaVInstallationRoot),
            OptimizationActionIds.RecommendGraphicsPreset => new GraphicsPresetRecommendationAction(
                dependencies.GpuDetails,
                dependencies.Cpu,
                dependencies.RamDetails,
                dependencies.DisplayConfiguration),
            OptimizationActionIds.DiagnoseTextureVramFit => new TextureVramFitDiagnosisAction(
                environment.LegacyGraphicsSettingsPath,
                dependencies.GpuDetails),
            OptimizationActionIds.DiagnoseCacheStorage => new CacheStorageDiagnosisAction(
                environment.FiveMAppRoot),
            OptimizationActionIds.DiagnoseInstallationHealth => new InstallationHealthDiagnosisAction(
                environment.FiveMInstallationRoot,
                environment.FiveMAppRoot),
            OptimizationActionIds.DiagnoseCrashPatterns => new CrashPatternDiagnosisAction(
                environment.FiveMAppRoot),
            OptimizationActionIds.TerminateStuckFiveMProcess => new StuckProcessTerminationAction(
                environment.FiveMInstallationRoot,
                dependencies.StuckProcess),
            OptimizationActionIds.RecreateFiveMLocalData => new RecreateFiveMLocalDataAction(
                environment.FiveMAppRoot,
                environment.FiveMInstallationRoot,
                dependencies.ProcessInspector,
                dependencies.FileTree),
            OptimizationActionIds.RepairStaleAuthData => new StaleAuthDataRepairAction(
                environment.FiveMAppRoot,
                environment.FiveMInstallationRoot,
                RosIdPath,
                DigitalEntitlementsRoot,
                AuthQuarantineRoot,
                dependencies.ProcessInspector),
            OptimizationActionIds.CleanUserTemporaryFiles => new UserTemporaryFilesCleanupAction(
                environment.UserTemporaryDirectory,
                TimeSpan.FromDays(plan.Options.TemporaryFileMinimumAgeDays),
                dependencies.FileTree),
            OptimizationActionIds.PruneLegacyCrashDumps => new LegacyCrashDumpsPruneAction(
                environment.FiveMAppRoot,
                environment.FiveMInstallationRoot,
                TimeSpan.FromDays(plan.Options.DiagnosticRetentionDays),
                dependencies.ProcessInspector,
                dependencies.FileTree),
            OptimizationActionIds.RepairLegacyServerCache => new LegacyServerCacheRepairAction(
                environment.FiveMAppRoot,
                environment.FiveMInstallationRoot,
                plan.Options.ServerCacheRepair,
                checked(plan.Options.ServerCacheThresholdGiB * GiB),
                dependencies.ProcessInspector,
                dependencies.FileTree),
            OptimizationActionIds.EnableGameMode => new GameModeRegistryAction(
                dependencies.Registry),
            OptimizationActionIds.PreferHighPerformanceGpu => new GpuPreferenceRegistryAction(
                dependencies.Registry,
                environment.FiveMExecutablePath,
                environment.FiveMInstallationRoot),
            OptimizationActionIds.DisableBackgroundCapture => new GameDvrRegistryAction(
                dependencies.Registry),
            OptimizationActionIds.EnableSessionPerformancePowerPlan =>
                new SessionPerformancePowerPlanAction(
                    dependencies.PowerPlans,
                    dependencies.PowerStatus),
            OptimizationActionIds.ApplyLightLegacyGraphics => new LegacyGraphicsPresetAction(
                environment.LegacyGraphicsSettingsPath,
                environment.FiveMInstallationRoot,
                OptimizationProfile.Light,
                GraphicsSettingsTarget.FiveM,
                dependencies.ProcessInspector,
                dependencies.GtaVProcessInspector),
            OptimizationActionIds.ApplyBalancedLegacyGraphics => new LegacyGraphicsPresetAction(
                environment.LegacyGraphicsSettingsPath,
                environment.FiveMInstallationRoot,
                OptimizationProfile.Balanced,
                GraphicsSettingsTarget.FiveM,
                dependencies.ProcessInspector,
                dependencies.GtaVProcessInspector),
            OptimizationActionIds.ApplyAggressiveLegacyGraphics => new LegacyGraphicsPresetAction(
                environment.LegacyGraphicsSettingsPath,
                environment.FiveMInstallationRoot,
                OptimizationProfile.Aggressive,
                GraphicsSettingsTarget.FiveM,
                dependencies.ProcessInspector,
                dependencies.GtaVProcessInspector),
            OptimizationActionIds.ApplyLightGtaVGraphics => new LegacyGraphicsPresetAction(
                environment.GtaVGraphicsSettingsPath,
                environment.GtaVInstallationRoot,
                OptimizationProfile.Light,
                GraphicsSettingsTarget.GtaV,
                dependencies.ProcessInspector,
                dependencies.GtaVProcessInspector),
            OptimizationActionIds.ApplyBalancedGtaVGraphics => new LegacyGraphicsPresetAction(
                environment.GtaVGraphicsSettingsPath,
                environment.GtaVInstallationRoot,
                OptimizationProfile.Balanced,
                GraphicsSettingsTarget.GtaV,
                dependencies.ProcessInspector,
                dependencies.GtaVProcessInspector),
            OptimizationActionIds.ApplyAggressiveGtaVGraphics => new LegacyGraphicsPresetAction(
                environment.GtaVGraphicsSettingsPath,
                environment.GtaVInstallationRoot,
                OptimizationProfile.Aggressive,
                GraphicsSettingsTarget.GtaV,
                dependencies.ProcessInspector,
                dependencies.GtaVProcessInspector),
            OptimizationActionIds.ApplyQualityLegacyGraphics => new LegacyGraphicsPresetAction(
                environment.LegacyGraphicsSettingsPath,
                environment.FiveMInstallationRoot,
                GraphicsSettingsTarget.FiveM,
                dependencies.ProcessInspector,
                dependencies.GtaVProcessInspector,
                OptimizationActionIds.ApplyQualityLegacyGraphics,
                LegacyGraphicsPresets.Quality,
                GraphicsPresetDirection.RaiseOnly),
            OptimizationActionIds.ApplyQualityGtaVGraphics => new LegacyGraphicsPresetAction(
                environment.GtaVGraphicsSettingsPath,
                environment.GtaVInstallationRoot,
                GraphicsSettingsTarget.GtaV,
                dependencies.ProcessInspector,
                dependencies.GtaVProcessInspector,
                OptimizationActionIds.ApplyQualityGtaVGraphics,
                LegacyGraphicsPresets.Quality,
                GraphicsPresetDirection.RaiseOnly),
            OptimizationActionIds.ApplyLegacyDisplayPreferences => new DisplayPreferencesAction(
                environment.LegacyGraphicsSettingsPath,
                environment.FiveMInstallationRoot,
                GraphicsSettingsTarget.FiveM,
                plan.Options.PreferWindowedMode,
                plan.Options.EnableVSync,
                dependencies.ProcessInspector,
                dependencies.GtaVProcessInspector),
            OptimizationActionIds.ApplyGtaVDisplayPreferences => new DisplayPreferencesAction(
                environment.GtaVGraphicsSettingsPath,
                environment.GtaVInstallationRoot,
                GraphicsSettingsTarget.GtaV,
                plan.Options.PreferWindowedMode,
                plan.Options.EnableVSync,
                dependencies.ProcessInspector,
                dependencies.GtaVProcessInspector),
            OptimizationActionIds.ApplyGtaVGraphicsLaunchParameters => new GtaVGraphicsLaunchParametersAction(
                environment.GtaVInstallationRoot,
                dependencies.DisplayConfiguration,
                dependencies.GtaVProcessInspector),
            OptimizationActionIds.ApplyGtaVDisplayLaunchParameters => new GtaVDisplayLaunchParametersAction(
                environment.GtaVInstallationRoot,
                plan.Options.PreferWindowedMode,
                plan.Options.PreferBorderlessWindow,
                plan.Options.GtaVLaunchDirectXVersion,
                dependencies.GtaVProcessInspector),
            OptimizationActionIds.ApplyGtaVRepairLaunchParameters => new GtaVRepairLaunchParametersAction(
                environment.GtaVInstallationRoot,
                plan.Options.UseGtaVSafeMode,
                plan.Options.UseGtaVMinimumSettings,
                plan.Options.UseGtaVAutoSettingsRebuild,
                dependencies.GtaVProcessInspector),
            OptimizationActionIds.ReduceWindowsVisualEffects => new VisualEffectsAction(
                dependencies.VisualEffects),
            _ => throw new InvalidOperationException(
                $"Core action '{actionId}' has no registered Windows handler.")
        };
    }

    private static void ValidatePlan(OptimizationPlanDto plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(plan.Options);
        ArgumentNullException.ThrowIfNull(plan.Actions);

        if (plan.PlanId == Guid.Empty
            || plan.SchemaVersion != ProductIdentity.PlanSchemaVersion
            || plan.CatalogVersion != ActionCatalog.CurrentVersion
            || plan.ProductName != ProductIdentity.Name
            || plan.ProductSubtitle != ProductIdentity.Subtitle)
        {
            throw new InvalidOperationException("The optimization plan identity or version is invalid.");
        }

        if (!plan.IsExecutable
            || plan.Edition != FiveMEdition.Legacy
            || plan.Blocks.Count != 0
            || plan.Actions.Count == 0)
        {
            throw new InvalidOperationException("Only an executable FiveM Legacy plan can be resolved.");
        }

        var canonical = new PlanBuilder().Build(new OptimizationPlanRequestDto
        {
            Profile = plan.Profile,
            Edition = plan.Edition,
            Options = plan.Options with { }
        });
        if (!canonical.IsExecutable
            || canonical.Actions.Count != plan.Actions.Count
            || canonical.RequiresElevation != plan.RequiresElevation
            || canonical.ContainsNonReversibleActions != plan.ContainsNonReversibleActions
            || canonical.MaximumRisk != plan.MaximumRisk)
        {
            throw new InvalidOperationException("The optimization plan summary does not match Core policy.");
        }

        for (var index = 0; index < canonical.Actions.Count; index++)
        {
            var supplied = plan.Actions[index];
            var expected = canonical.Actions[index];
            if (supplied.Sequence != index + 1
                || expected.Sequence != supplied.Sequence
                || !WindowsActionMetadata.MatchesCore(supplied.Metadata)
                || supplied.Metadata.Id != expected.Metadata.Id
                || supplied.Metadata.Version != expected.Metadata.Version)
            {
                throw new InvalidOperationException(
                    $"Action at sequence {index + 1} does not match the canonical Core plan.");
            }
        }
    }

    private static WindowsOptimizationEnvironment ValidateEnvironment(
        WindowsOptimizationEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var installationRoot = SafePath.Normalize(environment.FiveMInstallationRoot);
        var appRoot = SafePath.EnsureDescendant(installationRoot, environment.FiveMAppRoot);
        var executable = SafePath.EnsureDescendant(
            installationRoot,
            environment.FiveMExecutablePath);
        if (!Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("FiveMExecutablePath must point to an executable.", nameof(environment));
        }

        var settings = Path.GetFullPath(environment.LegacyGraphicsSettingsPath);
        if (!Path.GetFileName(settings).Equals(
            "gta5_settings.xml",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "LegacyGraphicsSettingsPath must point to gta5_settings.xml.",
                nameof(environment));
        }

        var gtaSettings = Path.GetFullPath(environment.GtaVGraphicsSettingsPath);
        if (!Path.GetFileName(gtaSettings).Equals("settings.xml", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "GtaVGraphicsSettingsPath deve apontar para settings.xml.",
                nameof(environment));
        }

        string? gtaRoot = null;
        string? gtaExecutable = null;
        if (environment.GtaVInstallationRoot is not null
            || environment.GtaVExecutablePath is not null)
        {
            if (string.IsNullOrWhiteSpace(environment.GtaVInstallationRoot)
                || string.IsNullOrWhiteSpace(environment.GtaVExecutablePath))
            {
                throw new ArgumentException(
                    "A raiz e o executável do GTA V devem ser informados juntos.",
                    nameof(environment));
            }

            gtaRoot = SafePath.Normalize(environment.GtaVInstallationRoot);
            gtaExecutable = SafePath.EnsureDescendant(gtaRoot, environment.GtaVExecutablePath);
            if (!Path.GetFileName(gtaExecutable).Equals("GTA5.exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "GtaVExecutablePath deve apontar para GTA5.exe.",
                    nameof(environment));
            }
        }

        return environment with
        {
            FiveMInstallationRoot = installationRoot,
            FiveMAppRoot = appRoot,
            FiveMExecutablePath = executable,
            LegacyGraphicsSettingsPath = settings,
            GtaVInstallationRoot = gtaRoot,
            GtaVExecutablePath = gtaExecutable,
            GtaVGraphicsSettingsPath = gtaSettings,
            UserTemporaryDirectory = SafePath.Normalize(environment.UserTemporaryDirectory),
            JournalDirectory = SafePath.Normalize(environment.JournalDirectory)
        };
    }
}

public sealed class WindowsOptimizationRuntime
{
    private readonly WindowsOptimizationActionFactory factory;

    private WindowsOptimizationRuntime(
        WindowsOptimizationActionFactory factory,
        WindowsActionCatalog catalog,
        WindowsTransactionEngine engine)
    {
        this.factory = factory;
        Catalog = catalog;
        Engine = engine;
    }

    public WindowsActionCatalog Catalog { get; }

    public WindowsTransactionEngine Engine { get; }

    public static WindowsOptimizationRuntime CreateDefault()
    {
        var environment = WindowsOptimizationEnvironment.DetectDefault();
        return Create(environment, WindowsOptimizationDependencies.CreateDefault(environment));
    }

    public static WindowsOptimizationRuntime CreateDefaultForCurrentUser()
    {
        return CreateDefault();
    }

    public static WindowsOptimizationRuntime Create(
        WindowsOptimizationEnvironment environment,
        WindowsOptimizationDependencies dependencies)
    {
        var factory = new WindowsOptimizationActionFactory(environment, dependencies);
        var catalog = new WindowsActionCatalog(factory.CreateCatalogActions());
        var engine = new WindowsTransactionEngine(catalog, dependencies.JournalStore);
        return new WindowsOptimizationRuntime(factory, catalog, engine);
    }

    public IReadOnlyList<IWindowsOptimizationAction> ResolveActions(OptimizationPlanDto plan)
    {
        return factory.Create(plan);
    }

    public IReadOnlyList<IWindowsOptimizationAction> ResolveAdministratorActions(
        OptimizationPlanDto plan)
    {
        return ResolveActions(plan)
            .Where(action => action.Metadata.RequiredPrivilege == RequiredPrivilege.Administrator)
            .ToArray();
    }

    public IReadOnlyList<IWindowsOptimizationAction> ResolveAdministratorActions(
        IEnumerable<(string Id, int Version)> requestedActions)
    {
        ArgumentNullException.ThrowIfNull(requestedActions);
        var resolved = new List<IWindowsOptimizationAction>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var request in requestedActions)
        {
            if (!seen.Add(request.Id))
            {
                throw new InvalidOperationException($"Action '{request.Id}' was requested more than once.");
            }

            var action = Catalog.GetRequired(request.Id, request.Version);
            if (action.Metadata.RequiredPrivilege != RequiredPrivilege.Administrator)
            {
                throw new UnauthorizedAccessException(
                    $"Action '{request.Id}' is not an administrator action.");
            }

            resolved.Add(action);
        }

        return resolved;
    }

    public Task<WindowsTransactionResult> ExecuteAsync(
        OptimizationPlanDto plan,
        WindowsActionContext context,
        WindowsTransactionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return Engine.ExecuteAsync(
            ResolveActions(plan),
            context,
            options,
            cancellationToken);
    }
}
