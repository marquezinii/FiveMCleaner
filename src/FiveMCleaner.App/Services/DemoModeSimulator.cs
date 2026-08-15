using FiveMCleaner.Contracts;

namespace FiveMCleaner.App.Services;

/// <summary>
/// Handles demo/simulation mode: produces a synthetic diagnostic snapshot
/// and simulates a plan execution with staged delays. Extracted from
/// <see cref="AppOptimizationService"/> to keep the main service focused on
/// real execution paths.
/// </summary>
internal sealed class DemoModeSimulator
{
    private readonly ILocalizationService localization;

    public DemoModeSimulator(ILocalizationService localization)
    {
        this.localization = localization;
    }

    public AppDiagnostic CreateDiagnostic()
    {
        return new AppDiagnostic
        {
            Edition = FiveMEdition.Legacy,
            IsFiveMRunning = false,
            FiveMRoot = null,
            GtaVDetected = true,
            GtaVIsRunning = false,
            GtaVExecutablePath = @"C:\Jogos\Grand Theft Auto V\GTA5.exe",
            GtaVGraphicsSettingsPath = @"C:\User\Documents\Rockstar Games\GTA V\settings.xml",
            CpuName = localization.GetString("Demo.Cpu"),
            GpuName = localization.GetString("Demo.Gpu"),
            GpuNames = [localization.GetString("Demo.Gpu")],
            TotalMemoryGiB = 16,
            AvailableMemoryGiB = 8,
            MemoryModuleLayout = "2×8 GB",
            LogicalProcessorCount = 12,
            FreeDiskGiB = 128,
            LegacyCacheBytes = 3L * 1024 * 1024 * 1024,
            OsLabel = "Windows 11",
            SystemArchitecture = "x64",
            ReadinessScore = 88,
            RecommendedProfile = OptimizationProfile.Balanced,
            PerformancePressure = PerformancePressureLevel.Moderate,
            StreamingSoftware = StreamingSoftwareClassifier.CreateSnapshot(
                [],
                [],
                [],
                DateTimeOffset.UtcNow),
            Notices = [localization.GetString("Demo.Notice")]
        };
    }

    public async Task<AppOptimizationResult> SimulatePlanAsync(
        OptimizationPlanDto plan,
        IProgress<AppProgressUpdate> progress,
        CancellationToken cancellationToken)
    {
        progress.Report(new AppProgressUpdate
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = AppProgressKind.Preparing,
            Percent = 2,
            Headline = localization.GetString("Runtime.PreparingSimulation"),
            Detail = localization.GetString("Runtime.SimulationSafe")
        });

        await Task.Delay(180, cancellationToken).ConfigureAwait(false);
        var actions = plan.Actions.OrderBy(action => action.Sequence).ToArray();
        for (var index = 0; index < actions.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var action = actions[index];
            progress.Report(new AppProgressUpdate
            {
                Timestamp = DateTimeOffset.UtcNow,
                Kind = AppProgressKind.Applying,
                Percent = 5d + (85d * (index + 1) / Math.Max(1, actions.Length)),
                Headline = localization.GetString("Runtime.SimulatingPlan"),
                Detail = localization.Format(
                    "Runtime.SimulationAction",
                    GetLocalizedActionName(action.Metadata)),
                ActionId = action.Metadata.Id
            });
            await Task.Delay(180, cancellationToken).ConfigureAwait(false);
        }

        progress.Report(new AppProgressUpdate
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = AppProgressKind.Verifying,
            Percent = 96,
            Headline = localization.GetString("Runtime.ValidatingSimulation"),
            Detail = localization.GetString("Runtime.SimulationNoWrites")
        });
        await Task.Delay(220, cancellationToken).ConfigureAwait(false);
        progress.Report(new AppProgressUpdate
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = AppProgressKind.Completed,
            Percent = 100,
            Headline = localization.GetString("Runtime.SimulationCompleted"),
            Detail = localization.GetString("Runtime.NoChangesApplied")
        });

        return new AppOptimizationResult
        {
            TransactionId = plan.PlanId,
            Succeeded = true,
            WasCancelled = false,
            Summary = $"{localization.GetString("Runtime.SimulationCompleted")}. "
                + localization.GetString("Runtime.NoChangesApplied"),
            CompletedActions = actions.Length,
            BytesFreed = 0
        };
    }

    private string GetLocalizedActionName(ActionMetadataDto action)
    {
        var key = $"Actions.{action.Id}.Name";
        var value = localization.GetString(key);
        return value == key ? action.Name : value;
    }
}
