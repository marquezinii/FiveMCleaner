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
        return new WindowsOptimizationEnvironment
        {
            FiveMInstallationRoot = installationRoot,
            FiveMAppRoot = Path.Combine(installationRoot, "FiveM.app"),
            FiveMExecutablePath = Path.Combine(installationRoot, "FiveM.exe"),
            LegacyGraphicsSettingsPath = Path.Combine(
                roamingAppData,
                "CitizenFX",
                "gta5_settings.xml"),
            UserTemporaryDirectory = Path.Combine(localAppData, "Temp"),
            JournalDirectory = Path.Combine(localAppData, "FiveMCleaner", "Transactions")
        };
    }
}

public sealed record WindowsOptimizationDependencies
{
    public required IRegistryStore Registry { get; init; }

    public required IFiveMProcessInspector ProcessInspector { get; init; }

    public required SafeFileTree FileTree { get; init; }

    public required IVisualEffectsController VisualEffects { get; init; }

    public required IPowerPlanController PowerPlans { get; init; }

    public required IPowerStatusProvider PowerStatus { get; init; }

    public required IWindowsTransactionJournalStore JournalStore { get; init; }

    public static WindowsOptimizationDependencies CreateDefault(
        WindowsOptimizationEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var commandRunner = new ProcessCommandRunner();
        return new WindowsOptimizationDependencies
        {
            Registry = new WindowsRegistryStore(),
            ProcessInspector = new WindowsFiveMProcessInspector(),
            FileTree = new SafeFileTree(),
            VisualEffects = new WindowsVisualEffectsController(),
            PowerPlans = new PowerCfgController(commandRunner),
            PowerStatus = new WindowsPowerStatusProvider(),
            JournalStore = new JsonWindowsTransactionJournalStore(environment.JournalDirectory)
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
                OptimizationProfile.Balanced,
                dependencies.ProcessInspector),
            new LegacyGraphicsPresetAction(
                environment.LegacyGraphicsSettingsPath,
                environment.FiveMInstallationRoot,
                OptimizationProfile.Aggressive,
                dependencies.ProcessInspector),
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
            OptimizationActionIds.ApplyBalancedLegacyGraphics => new LegacyGraphicsPresetAction(
                environment.LegacyGraphicsSettingsPath,
                environment.FiveMInstallationRoot,
                OptimizationProfile.Balanced,
                dependencies.ProcessInspector),
            OptimizationActionIds.ApplyAggressiveLegacyGraphics => new LegacyGraphicsPresetAction(
                environment.LegacyGraphicsSettingsPath,
                environment.FiveMInstallationRoot,
                OptimizationProfile.Aggressive,
                dependencies.ProcessInspector),
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

        return environment with
        {
            FiveMInstallationRoot = installationRoot,
            FiveMAppRoot = appRoot,
            FiveMExecutablePath = executable,
            LegacyGraphicsSettingsPath = settings,
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
