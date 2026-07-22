using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Windows;
using FiveMCleaner.Windows.Actions;
using FiveMCleaner.Windows.Engine;
using FiveMCleaner.Windows.Infrastructure;
using Microsoft.Win32;

namespace FiveMCleaner.App.Services;

public sealed class AppOptimizationService : IAppOptimizationService
{
    private readonly string appDataDirectory;
    private readonly string journalDirectory;
    private readonly string logsDirectory;
    private readonly string settingsPath;
    private readonly JsonSerializerOptions indentedJson;
    private readonly ElevatedBrokerClient brokerClient;
    private readonly ILocalizationService localization;
    private readonly bool demoMode;
    private readonly bool useSyntheticDiagnostic;
    private string? detectedLegacyRoot;

    public AppOptimizationService(
        bool demoMode = false,
        bool useSyntheticDiagnostic = false,
        ILocalizationService? localization = null)
    {
        this.demoMode = demoMode;
        this.useSyntheticDiagnostic = useSyntheticDiagnostic;
        this.localization = localization ?? LocalizationService.Current;
        appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductIdentity.Name);
        journalDirectory = Path.Combine(appDataDirectory, "Transactions");
        logsDirectory = Path.Combine(appDataDirectory, "Logs");
        settingsPath = Path.Combine(appDataDirectory, "settings.json");
        indentedJson = new JsonSerializerOptions(FiveMCleanerJson.Options) { WriteIndented = true };
        brokerClient = new ElevatedBrokerClient(appDataDirectory);
    }

    public string LogsDirectory => logsDirectory;

    public async Task<AppDiagnostic> DiagnoseAsync(CancellationToken cancellationToken = default)
    {
        if (demoMode && useSyntheticDiagnostic)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CreateDemoDiagnostic();
        }

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var installation = DetectFiveMInstallation();
            var gtaV = GtaVLocator.Detect(installation.Root);
            var gtaVIsRunning = new WindowsGtaVProcessInspector()
                .IsRunningFrom(gtaV.InstallationRoot);
            detectedLegacyRoot = installation.Edition == FiveMEdition.Legacy
                ? installation.Root
                : null;
            var memoryStatus = GetMemoryStatus();
            var systemDrive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            var cacheBytes = installation.Edition == FiveMEdition.Legacy && installation.Root is not null
                ? GetLegacyServerCacheBytes(installation.Root, cancellationToken)
                : 0L;
            var gpuNames = GetGpuNames();
            var gpuWasIdentified = gpuNames.Count > 0;
            var gpuName = gpuWasIdentified
                ? string.Join(" / ", gpuNames)
                : localization.GetString("Diagnosis.GpuFallback");
            var streamingSoftware = DetectStreamingSoftware(cancellationToken);
            var memoryGiB = memoryStatus.TotalPhysical / 1024d / 1024d / 1024d;
            var availableMemoryGiB = memoryStatus.AvailablePhysical / 1024d / 1024d / 1024d;
            var logicalProcessorCount = Math.Max(1, Environment.ProcessorCount);
            var freeDiskGiB = systemDrive.AvailableFreeSpace / 1024d / 1024d / 1024d;
            var running = IsFiveMRunning();

            var assessment = HardwareProfileAdvisor.Assess(
                memoryGiB,
                availableMemoryGiB,
                logicalProcessorCount,
                freeDiskGiB,
                installation.Edition,
                cacheBytes,
                gpuWasIdentified);
            var notices = new List<string>();
            notices.Add(gtaV.IsInstalled
                ? "GTA V Legacy detectado; executável e settings.xml entrarão nas ações compatíveis."
                : "O executável do GTA V Legacy não foi confirmado automaticamente.");
            if (cacheBytes >= 8L * 1024 * 1024 * 1024)
            {
                notices.Add("O cache regenerável de servidores está acima de 8 GB; o reparo inteligente pode liberar espaço.");
            }
            else if (freeDiskGiB < 15)
            {
                notices.Add("Há pouco espaço livre na unidade do Windows; limpezas seguras podem melhorar a responsividade geral.");
            }
            else
            {
                notices.Add("O PC está estável; o perfil sugerido prioriza consistência sem tweaks de risco.");
            }

            return new AppDiagnostic
            {
                Edition = installation.Edition,
                IsFiveMRunning = running,
                FiveMRoot = installation.Root,
                GtaVDetected = gtaV.IsInstalled,
                GtaVIsRunning = gtaVIsRunning,
                GtaVExecutablePath = gtaV.ExecutablePath,
                GtaVGraphicsSettingsPath = gtaV.GraphicsSettingsPath,
                CpuName = GetCpuName(),
                GpuName = gpuName,
                GpuNames = gpuNames,
                TotalMemoryGiB = memoryGiB,
                AvailableMemoryGiB = availableMemoryGiB,
                LogicalProcessorCount = logicalProcessorCount,
                FreeDiskGiB = freeDiskGiB,
                LegacyCacheBytes = cacheBytes,
                OsLabel = RuntimeInformation.OSDescription,
                SystemArchitecture = GetArchitectureLabel(),
                ReadinessScore = assessment.ReadinessScore,
                RecommendedProfile = assessment.RecommendedProfile,
                PerformancePressure = assessment.PerformancePressure,
                StreamingSoftware = streamingSoftware,
                Notices = notices
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (demoMode)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new AppSettings();
        }

        if (!File.Exists(settingsPath))
        {
            return new AppSettings();
        }

        try
        {
            await using var stream = new FileStream(
                settingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, indentedJson, cancellationToken)
                .ConfigureAwait(false) ?? new AppSettings();
        }
        catch (Exception exception) when (exception is JsonException
            or NotSupportedException
            or IOException)
        {
            return new AppSettings();
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (demoMode)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        Directory.CreateDirectory(appDataDirectory);
        var temporary = Path.Combine(appDataDirectory, $".settings.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, settings, indentedJson, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, settingsPath, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public Task<AppOptimizationResult> ExecuteAsync(
        OptimizationPlanDto plan,
        IProgress<AppProgressUpdate> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(progress);
        if (demoMode)
        {
            return SimulatePlanAsync(plan, progress, cancellationToken);
        }

        return ExecutePlanCoreAsync(plan, progress, cancellationToken);
    }

    private async Task<AppOptimizationResult> SimulatePlanAsync(
        OptimizationPlanDto plan,
        IProgress<AppProgressUpdate> progress,
        CancellationToken cancellationToken)
    {
        progress.Report(new AppProgressUpdate
        {
            Timestamp = DateTimeOffset.Now,
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
                Timestamp = DateTimeOffset.Now,
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
            Timestamp = DateTimeOffset.Now,
            Kind = AppProgressKind.Verifying,
            Percent = 96,
            Headline = localization.GetString("Runtime.ValidatingSimulation"),
            Detail = localization.GetString("Runtime.SimulationNoWrites")
        });
        await Task.Delay(220, cancellationToken).ConfigureAwait(false);
        progress.Report(new AppProgressUpdate
        {
            Timestamp = DateTimeOffset.Now,
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

    public async Task<IReadOnlyList<AppHistoryRecord>> LoadHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        if (demoMode)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return [];
        }

        if (!Directory.Exists(journalDirectory))
        {
            return [];
        }

        var records = new List<AppHistoryRecord>();
        foreach (var path in Directory.EnumerateFiles(journalDirectory, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Take(50))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = File.OpenRead(path);
                var journal = await JsonSerializer.DeserializeAsync<WindowsTransactionJournal>(
                    stream,
                    indentedJson,
                    cancellationToken).ConfigureAwait(false);
                if (journal is null)
                {
                    continue;
                }

                var profile = InferProfile(journal);
                var changed = journal.Actions.Count(action => action.Changed);
                var canRollback = journal.Actions.Any(action =>
                    action.Changed
                    && action.State == WindowsActionJournalState.Committed
                    && action.Reversibility is not (
                        ActionReversibility.Irreversible
                        or ActionReversibility.RebuildableData));
                records.Add(new AppHistoryRecord
                {
                    TransactionId = journal.TransactionId,
                    CreatedAt = journal.CreatedAtUtc,
                    Profile = profile,
                    State = TranslateState(journal.State),
                    ChangedActions = changed,
                    CanRollback = canRollback && journal.State is
                        WindowsTransactionState.Committed
                        or WindowsTransactionState.AwaitingElevationRollback
                        or WindowsTransactionState.AwaitingStandardRollback
                });
            }
            catch (JsonException)
            {
                // Ignore a single corrupt historical journal; the active transaction is unaffected.
            }
        }

        return records;
    }

    public Task<bool> RollbackAsync(
        Guid transactionId,
        IProgress<AppProgressUpdate> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (demoMode)
        {
            throw new InvalidOperationException(
                localization.GetString("Runtime.DemoHistoryDisabled"));
        }

        return RollbackCoreAsync(transactionId, progress, cancellationToken);
    }

    private AppDiagnostic CreateDemoDiagnostic()
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

    private static StreamingSoftwareSnapshot DetectStreamingSoftware(
        CancellationToken cancellationToken)
    {
        try
        {
            return new StreamingSoftwareDetector().Detect(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            return StreamingSoftwareClassifier.CreateSnapshot(
                [],
                [],
                [],
                DateTimeOffset.UtcNow,
                processScanComplete: false,
                installationScanComplete: false);
        }
    }

    private async Task<AppOptimizationResult> ExecutePlanCoreAsync(
        OptimizationPlanDto plan,
        IProgress<AppProgressUpdate> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(new AppProgressUpdate
        {
            Timestamp = DateTimeOffset.Now,
            Kind = AppProgressKind.Preparing,
            Percent = 2,
            Headline = localization.GetString("Runtime.ValidatingPlan"),
            Detail = localization.GetString("Runtime.ValidatingPlanDetail")
        });

        var runtime = CreateRuntimeForDetectedInstallation();
        var actionProgress = new InlineProgress<WindowsActionProgress>(update =>
        {
            var percent = update.TotalWeight > 0
                ? 5d + (65d * update.CompletedWeight / update.TotalWeight)
                : 5d;
            progress.Report(new AppProgressUpdate
            {
                Timestamp = DateTimeOffset.Now,
                Kind = AppProgressKind.Applying,
                Percent = Math.Clamp(percent, 5, 70),
                Headline = localization.GetString("Runtime.OptimizingSafely"),
                Detail = localization.Format(
                    update.Message.StartsWith("Concluído:", StringComparison.Ordinal)
                        ? "Runtime.ActionCompleted"
                        : "Runtime.ApplyingAction",
                    GetLocalizedActionName(update.ActionId)),
                ActionId = update.ActionId
            });
        });
        var context = new WindowsActionContext
        {
            TransactionId = plan.PlanId,
            StartedAtUtc = DateTimeOffset.UtcNow,
            IsElevated = false,
            Progress = actionProgress
        };
        var localResult = await runtime.ExecuteAsync(
            plan,
            context,
            new WindowsTransactionOptions
            {
                IncludeStandardUserActions = true,
                IncludeAdministratorActions = false,
                RollbackOnFailure = true
            },
            cancellationToken).ConfigureAwait(false);

        if (localResult.State is not (
            WindowsTransactionState.Committed
            or WindowsTransactionState.AwaitingElevation))
        {
            return await CreateResultFromJournalAsync(
                plan.PlanId,
                succeeded: false,
                wasCancelled: false,
                localResult.Error ?? localization.GetString("Runtime.LocalChangesReverted"),
                cancellationToken).ConfigureAwait(false);
        }

        if (localResult.DeferredAdministratorActionIds.Count > 0)
        {
            progress.Report(new AppProgressUpdate
            {
                Timestamp = DateTimeOffset.Now,
                Kind = AppProgressKind.Preparing,
                Percent = 71,
                Headline = localization.GetString("Runtime.WindowsConfirmation"),
                Detail = localization.GetString("Runtime.WindowsConfirmationDetail")
            });

            ElevatedBrokerResult elevated;
            try
            {
                elevated = await brokerClient.ExecuteAsync(plan, progress, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var rollback = await TryRollbackLocalPhaseAsync(runtime, plan.PlanId)
                    .ConfigureAwait(false);
                return await CreateResultFromJournalAsync(
                    plan.PlanId,
                    succeeded: false,
                    wasCancelled: true,
                    DescribeInterruptedBroker(
                        localization.GetString("Runtime.AdminConfirmationCancelled"),
                        rollback),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not (
                OutOfMemoryException or StackOverflowException or AccessViolationException))
            {
                var rollback = await TryRollbackLocalPhaseAsync(runtime, plan.PlanId)
                    .ConfigureAwait(false);
                return await CreateResultFromJournalAsync(
                    plan.PlanId,
                    succeeded: false,
                    wasCancelled: false,
                    DescribeInterruptedBroker(
                        localization.Format("Runtime.BrokerResultUnconfirmed", exception.Message),
                        rollback),
                    CancellationToken.None).ConfigureAwait(false);
            }

            if (!elevated.Succeeded)
            {
                var rollback = await runtime.Engine.RollbackAsync(
                    plan.PlanId,
                    isElevated: false,
                    new WindowsRollbackOptions
                    {
                        IncludeStandardUserActions = true,
                        IncludeAdministratorActions = false
                    },
                    CancellationToken.None).ConfigureAwait(false);
                var summary = elevated.WasCancelled
                    ? localization.GetString("Runtime.UacCancelledRestored")
                    : localization.Format("Runtime.AdminPhaseFailed", elevated.Message);
                if (rollback.State == WindowsTransactionState.RollbackFailed)
                {
                    summary += " " + localization.GetString("Runtime.RollbackJournalError");
                }

                return await CreateResultFromJournalAsync(
                    plan.PlanId,
                    succeeded: false,
                    wasCancelled: elevated.WasCancelled,
                    summary,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        progress.Report(new AppProgressUpdate
        {
            Timestamp = DateTimeOffset.Now,
            Kind = AppProgressKind.Completed,
            Percent = 100,
            Headline = localization.GetString("Runtime.PlanCompleted"),
            Detail = localization.GetString("Runtime.PlanCompletedDetail")
        });
        return await CreateResultFromJournalAsync(
            plan.PlanId,
            succeeded: true,
            wasCancelled: false,
            $"{localization.GetString("Runtime.PlanCompleted")}. "
                + localization.GetString("Runtime.PlanCompletedDetail"),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> RollbackCoreAsync(
        Guid transactionId,
        IProgress<AppProgressUpdate> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(new AppProgressUpdate
        {
            Timestamp = DateTimeOffset.Now,
            Kind = AppProgressKind.RollingBack,
            Percent = 5,
            Headline = localization.GetString("Runtime.PreparingRestore"),
            Detail = localization.Format("Runtime.ValidatingTransaction", transactionId.ToString("N"))
        });

        var runtime = CreateRuntimeForDetectedInstallation();
        var localResult = await runtime.Engine.RollbackAsync(
            transactionId,
            isElevated: false,
            new WindowsRollbackOptions
            {
                IncludeStandardUserActions = true,
                IncludeAdministratorActions = false
            },
            cancellationToken).ConfigureAwait(false);
        if (localResult.State == WindowsTransactionState.RollbackFailed)
        {
            throw new InvalidOperationException(localization.GetString("Runtime.RollbackConflict"));
        }

        if (localResult.State == WindowsTransactionState.AwaitingElevationRollback)
        {
            progress.Report(new AppProgressUpdate
            {
                Timestamp = DateTimeOffset.Now,
                Kind = AppProgressKind.RollingBack,
                Percent = 70,
                Headline = localization.GetString("Runtime.ConfirmRestore"),
                Detail = localization.GetString("Runtime.ConfirmRestoreDetail")
            });
            var elevated = await brokerClient.RollbackAsync(
                transactionId,
                progress,
                cancellationToken).ConfigureAwait(false);
            if (!elevated.Succeeded)
            {
                if (elevated.WasCancelled)
                {
                    progress.Report(new AppProgressUpdate
                    {
                        Timestamp = DateTimeOffset.Now,
                        Kind = AppProgressKind.Warning,
                        Percent = 72,
                        Headline = localization.GetString("Runtime.AdminRestorePending"),
                        Detail = localization.GetString("Runtime.AdminRestorePendingDetail")
                    });
                    return false;
                }

                throw new InvalidOperationException(elevated.Message);
            }
        }

        progress.Report(new AppProgressUpdate
        {
            Timestamp = DateTimeOffset.Now,
            Kind = AppProgressKind.Completed,
            Percent = 100,
            Headline = localization.GetString("Runtime.RestoreCompleted"),
            Detail = localization.GetString("Runtime.RestoreCompletedDetail")
        });
        return true;
    }

    private WindowsOptimizationRuntime CreateRuntimeForDetectedInstallation()
    {
        var environment = WindowsOptimizationEnvironment.DetectDefault();
        var root = detectedLegacyRoot;
        if (!string.IsNullOrWhiteSpace(root))
        {
            var fullRoot = Path.GetFullPath(root);
            var appRoot = Path.Combine(fullRoot, "FiveM.app");
            var executable = Path.Combine(fullRoot, "FiveM.exe");
            if (Directory.Exists(appRoot))
            {
                var gtaV = GtaVLocator.Detect(fullRoot);
                environment = environment with
                {
                    FiveMInstallationRoot = fullRoot,
                    FiveMAppRoot = appRoot,
                    FiveMExecutablePath = executable,
                    GtaVInstallationRoot = gtaV.InstallationRoot,
                    GtaVExecutablePath = gtaV.ExecutablePath,
                    GtaVGraphicsSettingsPath = gtaV.GraphicsSettingsPath
                };
            }
        }

        return WindowsOptimizationRuntime.Create(
            environment,
            WindowsOptimizationDependencies.CreateDefault(environment));
    }

    private static async Task<WindowsTransactionResult?> TryRollbackLocalPhaseAsync(
        WindowsOptimizationRuntime runtime,
        Guid transactionId)
    {
        try
        {
            return await runtime.Engine.RollbackAsync(
                transactionId,
                isElevated: false,
                new WindowsRollbackOptions
                {
                    IncludeStandardUserActions = true,
                    IncludeAdministratorActions = false
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private string DescribeInterruptedBroker(
        string reason,
        WindowsTransactionResult? rollback)
    {
        return rollback?.State switch
        {
            WindowsTransactionState.RolledBack =>
                localization.Format("Runtime.Interrupted.RolledBack", reason),
            WindowsTransactionState.AwaitingElevationRollback =>
                localization.Format("Runtime.Interrupted.AdminPending", reason),
            WindowsTransactionState.RollbackFailed =>
                localization.Format("Runtime.Interrupted.RollbackFailed", reason),
            null =>
                localization.Format("Runtime.Interrupted.Unconfirmed", reason),
            _ =>
                localization.Format("Runtime.Interrupted.CheckHistory", reason)
        };
    }

    private async Task<AppOptimizationResult> CreateResultFromJournalAsync(
        Guid transactionId,
        bool succeeded,
        bool wasCancelled,
        string summary,
        CancellationToken cancellationToken)
    {
        var journal = await LoadJournalAsync(transactionId, cancellationToken).ConfigureAwait(false);
        return new AppOptimizationResult
        {
            TransactionId = transactionId,
            Succeeded = succeeded,
            WasCancelled = wasCancelled,
            Summary = summary,
            CompletedActions = journal?.Actions.Count(action =>
                action.State == WindowsActionJournalState.Committed) ?? 0,
            BytesFreed = journal is null ? 0 : SumCommittedCleanupBytes(journal)
        };
    }

    private async Task<WindowsTransactionJournal?> LoadJournalAsync(
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(journalDirectory, $"{transactionId:N}.json");
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<WindowsTransactionJournal>(
            stream,
            indentedJson,
            cancellationToken).ConfigureAwait(false);
    }

    private static long SumCommittedCleanupBytes(WindowsTransactionJournal journal)
    {
        long total = 0;
        var cleanupIds = new HashSet<string>(StringComparer.Ordinal)
        {
            OptimizationActionIds.CleanUserTemporaryFiles,
            OptimizationActionIds.PruneLegacyCrashDumps,
            OptimizationActionIds.RepairLegacyServerCache
        };

        foreach (var entry in journal.Actions.Where(entry =>
                     entry.State == WindowsActionJournalState.Committed
                     && cleanupIds.Contains(entry.ActionId)
                     && !string.IsNullOrWhiteSpace(entry.SnapshotJson)))
        {
            try
            {
                using var document = JsonDocument.Parse(entry.SnapshotJson!);
                if (!document.RootElement.TryGetProperty("scopes", out var scopes))
                {
                    continue;
                }

                foreach (var scope in scopes.EnumerateArray())
                {
                    if (!scope.TryGetProperty("files", out var files))
                    {
                        continue;
                    }

                    foreach (var file in files.EnumerateArray())
                    {
                        if (file.TryGetProperty("length", out var length)
                            && length.TryGetInt64(out var bytes)
                            && bytes > 0)
                        {
                            total = checked(total + bytes);
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is JsonException or OverflowException)
            {
                // A contagem visual é opcional; o journal continua sendo a fonte de verdade.
            }
        }

        return total;
    }

    private static (FiveMEdition Edition, string? Root) DetectFiveMInstallation()
    {
        var candidates = new List<string>();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        candidates.Add(Path.Combine(localAppData, "FiveM"));

        foreach (var registryView in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView);
            using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null)
            {
                continue;
            }

            foreach (var subkeyName in uninstall.GetSubKeyNames())
            {
                using var subkey = uninstall.OpenSubKey(subkeyName);
                var displayName = subkey?.GetValue("DisplayName") as string;
                var installLocation = subkey?.GetValue("InstallLocation") as string;
                if (!string.IsNullOrWhiteSpace(displayName)
                    && displayName.Contains("FiveM", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(installLocation))
                {
                    if (displayName.Contains("Enhanced", StringComparison.OrdinalIgnoreCase))
                    {
                        return (FiveMEdition.Enhanced, Path.GetFullPath(installLocation));
                    }

                    candidates.Add(installLocation);
                }
            }
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var fullPath = Path.GetFullPath(candidate);
                if (Directory.Exists(Path.Combine(fullPath, "FiveM.app", "data")))
                {
                    return (FiveMEdition.Legacy, fullPath);
                }
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                // Ignore malformed registry entries and continue with known locations.
            }
        }

        var enhancedCandidate = Path.Combine(localAppData, "FiveM Enhanced");
        return Directory.Exists(enhancedCandidate)
            ? (FiveMEdition.Enhanced, enhancedCandidate)
            : (FiveMEdition.Unknown, null);
    }

    private static long GetLegacyServerCacheBytes(string root, CancellationToken cancellationToken)
    {
        var dataRoot = Path.Combine(root, "FiveM.app", "data");
        var allowed = new[] { "server-cache", "server-cache-priv" };
        long total = 0;
        foreach (var name in allowed)
        {
            var path = Path.Combine(dataRoot, name);
            if (!Directory.Exists(path))
            {
                continue;
            }

            var rootInfo = new DirectoryInfo(path);
            if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            var pending = new Stack<DirectoryInfo>();
            pending.Push(rootInfo);
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = pending.Pop();
                IEnumerable<FileSystemInfo> entries;
                try
                {
                    entries = directory.EnumerateFileSystemInfos();
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    if (entry is FileInfo file)
                    {
                        total += file.Length;
                    }
                    else if (entry is DirectoryInfo child)
                    {
                        pending.Push(child);
                    }
                }
            }
        }

        return total;
    }

    private static bool IsFiveMRunning()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }

        foreach (var process in processes)
        {
            using (process)
            try
            {
                if (WindowsFiveMProcessInspector.LooksLikeFiveMProcessName(process.ProcessName))
                {
                    return true;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException
                or System.ComponentModel.Win32Exception
                or NotSupportedException)
            {
            }
        }

        return false;
    }

    private string GetLocalizedActionName(ActionMetadataDto action)
    {
        return GetLocalizedActionName(action.Id, action.Name);
    }

    private string GetLocalizedActionName(string actionId)
    {
        var fallback = ActionCatalog.Current.TryGet(actionId, out var definition)
            ? definition!.Name
            : actionId;
        return GetLocalizedActionName(actionId, fallback);
    }

    private string GetLocalizedActionName(string actionId, string fallback)
    {
        var key = $"Actions.{actionId}.Name";
        var value = localization.GetString(key);
        return value == key ? fallback : value;
    }

    private string GetCpuName()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return (key?.GetValue("ProcessorNameString") as string)?.Trim()
            ?? localization.GetString("Diagnosis.CpuUnknown");
    }

    private IReadOnlyList<string> GetGpuNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var video = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Video");
            if (video is null)
            {
                return [];
            }

            foreach (var deviceKeyName in video.GetSubKeyNames())
            {
                using var device = video.OpenSubKey(deviceKeyName);
                if (device is null)
                {
                    continue;
                }

                foreach (var adapterKeyName in device.GetSubKeyNames()
                             .Where(name => name.Length == 4 && name.All(char.IsDigit)))
                {
                    using var adapter = device.OpenSubKey(adapterKeyName);
                    var name = (adapter?.GetValue("DriverDesc") as string)?.Trim();
                    if (!string.IsNullOrWhiteSpace(name)
                        && !name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase))
                    {
                        names.Add(name);
                    }
                }
            }
        }
        catch
        {
            // O diagnóstico continua sem iniciar PowerShell, WMI ou ferramentas externas.
        }

        return names.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string GetArchitectureLabel() => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.X86 => "x86",
        Architecture.Arm64 => "ARM64",
        Architecture.Arm => "ARM",
        _ => RuntimeInformation.OSArchitecture.ToString()
    };

    private MemoryStatusEx GetMemoryStatus()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status))
        {
            throw new InvalidOperationException(localization.GetString("Diagnosis.MemoryUnavailable"));
        }

        return status;
    }

    private static OptimizationProfile InferProfile(WindowsTransactionJournal journal)
    {
        return journal.Actions.Any(action => action.ActionId.Contains("aggressive", StringComparison.Ordinal))
            ? OptimizationProfile.Aggressive
            : journal.Actions.Any(action => action.ActionId.Contains("balanced", StringComparison.Ordinal)
                || action.ActionId.Contains("background-capture", StringComparison.Ordinal)
                || action.ActionId.Contains("power", StringComparison.Ordinal))
                ? OptimizationProfile.Balanced
                : OptimizationProfile.Light;
    }

    private string TranslateState(WindowsTransactionState state) => localization.GetString(state switch
    {
        WindowsTransactionState.Committed => "History.State.Committed",
        WindowsTransactionState.AwaitingElevation => "History.State.AwaitingUac",
        WindowsTransactionState.AwaitingElevationRollback => "History.State.AdminRollbackPending",
        WindowsTransactionState.AwaitingStandardRollback => "History.State.LocalRollbackPending",
        WindowsTransactionState.RolledBack => "History.State.RolledBack",
        WindowsTransactionState.RollbackFailed => "History.State.RollbackFailed",
        WindowsTransactionState.Failed => "History.State.FailedSafely",
        _ => "History.State.Interrupted"
    });

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> callback;

        public InlineProgress(Action<T> callback)
        {
            this.callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public void Report(T value) => callback(value);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
