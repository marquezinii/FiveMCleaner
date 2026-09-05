using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Management;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ralven.Contracts;
using Ralven.Core.Catalog;
using Ralven.Windows;
using Ralven.Windows.Actions;
using Ralven.Windows.Diagnostics;
using Ralven.Windows.Engine;
using Ralven.Windows.Infrastructure;
using Microsoft.Win32;

namespace Ralven.App.Services;

public sealed class AppOptimizationService : IAppOptimizationService
{
    private readonly string appDataDirectory;
    private readonly string journalDirectory;
    private readonly string logsDirectory;
    private readonly string settingsPath;
    private readonly JsonSerializerOptions indentedJson;
    private readonly ElevatedBrokerClient brokerClient;
    private readonly ILocalizationService localization;
    private readonly DemoModeSimulator demoSimulator;
    private readonly ResourceComparisonCapture resourceComparison;
    private readonly bool demoMode;
    private readonly bool useSyntheticDiagnostic;
    private readonly Func<Guid, bool> administratorReceiptExists;
    private string? detectedLegacyRoot;

    public AppOptimizationService(
        bool demoMode = false,
        bool useSyntheticDiagnostic = false,
        ILocalizationService? localization = null)
        : this(demoMode, useSyntheticDiagnostic, localization, appDataDirectoryOverride: null, null)
    {
    }

    internal AppOptimizationService(
        string appDataDirectory,
        ILocalizationService? localization = null,
        Func<Guid, bool>? administratorReceiptExists = null)
        : this(
            demoMode: false,
            useSyntheticDiagnostic: false,
            localization,
            appDataDirectory,
            administratorReceiptExists)
    {
    }

    private AppOptimizationService(
        bool demoMode,
        bool useSyntheticDiagnostic,
        ILocalizationService? localization,
        string? appDataDirectoryOverride,
        Func<Guid, bool>? administratorReceiptExists)
    {
        this.demoMode = demoMode;
        this.useSyntheticDiagnostic = useSyntheticDiagnostic;
        this.localization = localization ?? LocalizationService.Current;
        this.administratorReceiptExists = administratorReceiptExists ?? HasAdministratorReceipt;
        appDataDirectory = appDataDirectoryOverride is null
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ProductIdentity.Name)
            : Path.GetFullPath(appDataDirectoryOverride);
        journalDirectory = Path.Combine(appDataDirectory, "Transactions");
        logsDirectory = Path.Combine(appDataDirectory, "Logs");
        settingsPath = Path.Combine(appDataDirectory, "settings.json");
        indentedJson = new JsonSerializerOptions(RalvenJson.Options) { WriteIndented = true };
        brokerClient = new ElevatedBrokerClient(appDataDirectory, localization);
        demoSimulator = new DemoModeSimulator(this.localization);
        resourceComparison = new ResourceComparisonCapture(this.localization);
    }

    public string LogsDirectory => logsDirectory;

    internal Func<CancellationToken, Task<bool>> AuthorizePro { private get; init; } = _ => Task.FromResult(false);

    /// <summary>
    /// Read settings leniently. Writing stays strict
    /// (<see cref="indentedJson"/>), but a settings.json that drifted from the
    /// current schema must never wipe the user's stored preferences: a file
    /// written by a newer build (unknown members), hand-edited (comments,
    /// differently-cased keys) or edited by another tool used to throw a
    /// <see cref="JsonException"/> under the strict options, the catch in
    /// <see cref="LoadSettingsAsync"/> then returned a fresh
    /// <see cref="AppSettings"/>, silently re-arming the privacy consent
    /// screen and flipping the declined telemetry toggles back to their
    /// defaults. Unknown members are skipped, keys match case-insensitively
    /// and comments are tolerated; only genuinely unparseable content still
    /// falls through to the defaults.
    /// </summary>
    private static readonly JsonSerializerOptions SettingsReadOptions = new(RalvenJson.Options)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };

    /// <summary>
    /// Pure settings deserialization, exposed for tests so the "schema drift
    /// must not reset stored preferences" contract can be locked down without
    /// touching the real LocalApplicationData path.
    /// </summary>
    internal static AppSettings DeserializeSettings(string json) =>
        JsonSerializer.Deserialize<AppSettings>(json, SettingsReadOptions) ?? new AppSettings();

    public async Task<AppDiagnostic> DiagnoseAsync(CancellationToken cancellationToken = default)
    {
        if (demoMode && useSyntheticDiagnostic)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return demoSimulator.CreateDiagnostic();
        }

        return await Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Run independent I/O-bound operations concurrently to reduce total diagnosis time
            var installationTask = Task.Run(() => DetectFiveMInstallation(), cancellationToken);
            var memoryStatusTask = Task.Run(() => NativeMemoryStatus.Query(), cancellationToken);
            var gpuDetailsTask = Task.Run(
                () => new WindowsGpuDetailsInspector().GetSnapshot(),
                cancellationToken);
            var cpuDetailsTask = Task.Run(
                () => new WindowsCpuInspector().GetSnapshot(),
                cancellationToken);
            var cpuNameTask = Task.Run(() => ResourceComparisonCapture.GetCpuName(localization), cancellationToken);
            var memoryLayoutTask = Task.Run(GetMemoryModuleLayout, cancellationToken);
            var osLabelTask = Task.Run(GetOperatingSystemLabel, cancellationToken);
            var archLabelTask = Task.Run(GetArchitectureLabel, cancellationToken);

            var installation = await installationTask.ConfigureAwait(false);
            var gtaV = GtaVLocator.Detect(installation.Root);
            var gtaVIsRunning = new WindowsGtaVProcessInspector()
                .IsRunningFrom(gtaV.InstallationRoot);
            detectedLegacyRoot = installation.Edition == FiveMEdition.Legacy
                ? installation.Root
                : null;

            var memoryStatus = await memoryStatusTask.ConfigureAwait(false);
            var systemDrive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            var cacheBytes = installation.Edition == FiveMEdition.Legacy && installation.Root is not null
                ? GetLegacyServerCacheBytes(installation.Root, cancellationToken)
                : 0L;

            var gpuDetails = await gpuDetailsTask.ConfigureAwait(false);
            var cpuDetails = await cpuDetailsTask.ConfigureAwait(false);
            var gpuNames = gpuDetails
                .Select(gpu => gpu.DriverDescription)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var gpuWasIdentified = gpuNames.Length > 0;
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
                freeDiskGiB,
                cpuDetails,
                gpuDetails);

            var notices = BuildDiagnosticNotices(gtaV, cacheBytes, freeDiskGiB);

            // Await remaining parallel tasks
            var cpuName = await cpuNameTask.ConfigureAwait(false);
            var memoryModuleLayout = await memoryLayoutTask.ConfigureAwait(false);
            var osLabel = await osLabelTask.ConfigureAwait(false);
            var archLabel = await archLabelTask.ConfigureAwait(false);

            return new AppDiagnostic
            {
                Edition = installation.Edition,
                IsFiveMRunning = running,
                FiveMRoot = installation.Root,
                GtaVDetected = gtaV.IsInstalled,
                GtaVIsRunning = gtaVIsRunning,
                GtaVExecutablePath = gtaV.ExecutablePath,
                GtaVGraphicsSettingsPath = gtaV.GraphicsSettingsPath,
                CpuName = cpuName,
                GpuName = gpuName,
                GpuNames = gpuNames,
                TotalMemoryGiB = memoryGiB,
                AvailableMemoryGiB = availableMemoryGiB,
                MemoryModuleLayout = memoryModuleLayout,
                LogicalProcessorCount = logicalProcessorCount,
                FreeDiskGiB = freeDiskGiB,
                LegacyCacheBytes = cacheBytes,
                OsLabel = osLabel,
                SystemArchitecture = archLabel,
                ReadinessScore = assessment.ReadinessScore,
                RecommendedProfile = assessment.RecommendedProfile,
                PerformancePressure = assessment.PerformancePressure,
                StreamingSoftware = streamingSoftware,
                Notices = notices
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    public bool SettingsFileExists() => !demoMode && File.Exists(settingsPath);

    private static IReadOnlyList<string> BuildDiagnosticNotices(
        GtaVInstallationInfo gtaV,
        long cacheBytes,
        double freeDiskGiB)
    {
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

        return notices;
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
            var json = await File.ReadAllTextAsync(settingsPath, cancellationToken)
                .ConfigureAwait(false);
            return DeserializeSettings(json);
        }
        catch (Exception exception) when (exception is JsonException
            or NotSupportedException
            or IOException)
        {
            // A leitura tolerante cobre arquivos fora do schema atual; este
            // caminho só é atingido por conteúdo genuinamente ilegível
            // (JSON truncado/corrompido), em que não há valores a preservar.
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
            // Best-effort: settings are already durable once Move succeeds, so
            // a failed temp cleanup must not surface as a failed save.
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException)
            {
            }
        }
    }

    public async Task<AppOptimizationResult> ExecuteAsync(
        OptimizationPlanDto plan,
        IProgress<AppProgressUpdate> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(progress);
        cancellationToken.ThrowIfCancellationRequested();
        if (plan.PersonalPreferences is not null && !await AuthorizePro(cancellationToken).ConfigureAwait(false))
        {
            throw new ProAccessRequiredException(localization.GetString("Ultra.AccessRequired"));
        }
        if (demoMode)
        {
            return await demoSimulator.SimulatePlanAsync(plan, progress, cancellationToken).ConfigureAwait(false);
        }

        return await ExecutePlanCoreAsync(plan, progress, cancellationToken).ConfigureAwait(false);
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

                var profile = journal.Profile ?? InferProfile(journal);
                var changed = journal.Actions.Count(action => action.Changed);
                var canRollback = journal.Actions.Any(CanOfferRollback);
                var requiresAdministratorReceipt = journal.Actions.Any(action =>
                    action.RequiredPrivilege == RequiredPrivilege.Administrator
                    && CanOfferRollback(action));
                var hasRequiredReceipt = !requiresAdministratorReceipt
                    || administratorReceiptExists(journal.TransactionId);
                records.Add(new AppHistoryRecord
                {
                    TransactionId = journal.TransactionId,
                    CreatedAt = journal.CreatedAtUtc,
                    Profile = profile,
                    PersonalUsage = journal.PersonalUsage,
                    Kind = IsWindowsGamingControlsTransaction(journal)
                        ? AppHistoryKind.WindowsGaming
                        : AppHistoryKind.Optimization,
                    State = hasRequiredReceipt
                        ? TranslateState(journal.State)
                        : localization.GetString("History.State.AdminReceiptMissing"),
                    ChangedActions = changed,
                    CanRollback = canRollback && hasRequiredReceipt && journal.State is
                        TransactionState.Committed
                        or TransactionState.CommittedWithErrors
                        or TransactionState.AwaitingElevationRollback
                        or TransactionState.AwaitingStandardRollback
                        or TransactionState.RollbackFailed
                });
            }
            catch (Exception exception) when (exception is JsonException
                or NotSupportedException
                or IOException
                or UnauthorizedAccessException
                or SecurityException)
            {
                // Ignore one unreadable, corrupt, or incompatible historical journal; the active transaction is unaffected.
            }
        }

        return records;
    }

    public async Task<OptimizationReportDto?> LoadReportAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        if (demoMode || transactionId == Guid.Empty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }

        try
        {
            var journal = await LoadJournalAsync(transactionId, cancellationToken).ConfigureAwait(false);
            return journal?.TransactionId == transactionId
                ? OptimizationReportBuilder.Build(journal, journal.Profile ?? InferProfile(journal))
                : null;
        }
        catch (Exception exception) when (exception is JsonException
            or NotSupportedException
            or IOException
            or UnauthorizedAccessException
            or SecurityException)
        {
            return null;
        }
    }

    private static bool HasAdministratorReceipt(Guid transactionId)
    {
        try
        {
            using var localMachine = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine,
                RegistryView.Registry64);
            using var key = localMachine.OpenSubKey(
                ProductIdentity.AdministratorReceiptRegistryPath,
                writable: false);
            return key?.GetValue(
                transactionId.ToString("N"),
                null,
                RegistryValueOptions.DoNotExpandEnvironmentNames) is byte[] { Length: > 0 };
        }
        catch (Exception exception) when (exception is IOException
            or SecurityException
            or UnauthorizedAccessException)
        {
            return false;
        }
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

    public async Task<AppGtaVBenchmarkResult> RunGtaVBenchmarkAsync(
        int iterations,
        CancellationToken cancellationToken = default)
    {
        if (iterations < 1 || iterations > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations));
        }

        if (demoMode)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new AppGtaVBenchmarkResult
            {
                Succeeded = false,
                FailureReason = "demo-mode",
                Iterations = []
            };
        }

        var gtaV = GtaVLocator.Detect(detectedLegacyRoot);
        if (!gtaV.IsInstalled || gtaV.ExecutablePath is null)
        {
            return new AppGtaVBenchmarkResult
            {
                Succeeded = false,
                FailureReason = "gtav-not-detected",
                Iterations = []
            };
        }

        var running = new WindowsGtaVProcessInspector().IsRunningFrom(gtaV.InstallationRoot);
        if (running)
        {
            return new AppGtaVBenchmarkResult
            {
                Succeeded = false,
                FailureReason = "gtav-still-running",
                Iterations = []
            };
        }

        var runner = new WindowsGtaVBenchmarkRunner();
        var result = await runner.RunAsync(
            gtaV.ExecutablePath,
            iterations,
            TimeSpan.FromMinutes(5),
            cancellationToken).ConfigureAwait(false);

        return new AppGtaVBenchmarkResult
        {
            Succeeded = result.Succeeded,
            FailureReason = result.FailureReason,
            Iterations = result.Iterations.Select(ToAppIteration).ToArray(),
            Median = result.Median is null ? null : ToAppIteration(result.Median)
        };
    }

    private static AppGtaVBenchmarkIteration ToAppIteration(GtaVBenchmarkIterationResult iteration)
    {
        return new AppGtaVBenchmarkIteration(
            iteration.AverageFps,
            iteration.MinimumFps,
            iteration.OnePercentLowFps,
            iteration.PointOnePercentLowFps,
            iteration.AverageFrametimeMs,
            iteration.PeakFrametimeMs,
            iteration.SampleCount);
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
        ReportPreparing(progress);

        var beforeSnapshot = resourceComparison.TryCaptureSnapshot();
        var runtime = CreateRuntimeForPlan(plan);
        var localResult = await ExecuteLocalPhaseAsync(
            runtime,
            plan,
            progress,
            cancellationToken).ConfigureAwait(false);

        if (localResult.State is not (
            TransactionState.Committed
            or TransactionState.CommittedWithErrors
            or TransactionState.AwaitingElevation))
        {
            return await CreateResultFromJournalAsync(
                plan.PlanId,
                plan.Profile,
                succeeded: false,
                wasCancelled: false,
                localResult.Error ?? localization.GetString("Runtime.LocalChangesReverted"),
                cancellationToken).ConfigureAwait(false);
        }

        if (localResult.DeferredAdministratorActionIds.Count > 0)
        {
            var elevatedResult = await ExecuteElevatedPhaseAsync(
                runtime,
                plan,
                progress,
                cancellationToken).ConfigureAwait(false);
            if (elevatedResult is not null)
            {
                return elevatedResult;
            }
        }

        // O sucesso final é decidido pelo relatório do journal: uma run com
        // qualquer ação falhada nunca é reportada como totalmente concluída.
        var runSucceeded = await LoadFinalRunSucceededAsync(plan, cancellationToken).ConfigureAwait(false);

        ReportCompletion(progress, runSucceeded);

        var comparison = await resourceComparison.CaptureComparisonAsync(beforeSnapshot).ConfigureAwait(false);

        var result = await CreateResultFromJournalAsync(
            plan.PlanId,
            plan.Profile,
            succeeded: runSucceeded,
            wasCancelled: false,
            $"{localization.GetString(runSucceeded ? "Runtime.PlanCompleted" : "Runtime.PlanCompletedWithErrors")}. "
                + localization.GetString(
                    runSucceeded ? "Runtime.PlanCompletedDetail" : "Runtime.PlanCompletedWithErrorsDetail"),
            cancellationToken).ConfigureAwait(false);
        return comparison is null ? result : result with { Comparison = comparison };
    }

    private void ReportPreparing(IProgress<AppProgressUpdate> progress)
    {
        progress.Report(new AppProgressUpdate
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = AppProgressKind.Preparing,
            Percent = 2,
            Headline = localization.GetString("Runtime.ValidatingPlan"),
            Detail = localization.GetString("Runtime.ValidatingPlanDetail")
        });
    }

    private async Task<WindowsTransactionResult> ExecuteLocalPhaseAsync(
        WindowsOptimizationRuntime runtime,
        OptimizationPlanDto plan,
        IProgress<AppProgressUpdate> progress,
        CancellationToken cancellationToken)
    {
        var actionProgress = new InlineProgress<WindowsActionProgress>(update =>
        {
            var percent = update.TotalWeight > 0
                ? 5d + (65d * update.CompletedWeight / update.TotalWeight)
                : 5d;
            var actionName = GetLocalizedActionName(update.ActionId);
            progress.Report(new AppProgressUpdate
            {
                Timestamp = DateTimeOffset.UtcNow,
                Kind = AppProgressKind.Applying,
                Percent = Math.Clamp(percent, 5, 70),
                Headline = actionName,
                Detail = localization.Format(DetailKeyFor(update.Outcome), actionName),
                ActionId = update.ActionId,
                CompletedSteps = update.CompletedSteps,
                TotalSteps = update.TotalSteps,
                Outcome = update.Outcome
            });
        });
        var context = new WindowsActionContext
        {
            TransactionId = plan.PlanId,
            StartedAtUtc = DateTimeOffset.UtcNow,
            IsElevated = false,
            Progress = actionProgress
        };
        return await runtime.ExecuteAsync(
            plan,
            context,
            new WindowsTransactionOptions
            {
                IncludeStandardUserActions = true,
                IncludeAdministratorActions = false,
                IsolateFailures = true
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executa a fase administrativa no broker elevado. Retorna um resultado
    /// final quando a fase termina (cancelada, falhou ou foi rejeitada pelo
    /// UAC); retorna <see langword="null"/> quando a fase elevada concluiu com
    /// sucesso e a orquestração deve prosseguir.
    /// </summary>
    private async Task<AppOptimizationResult?> ExecuteElevatedPhaseAsync(
        WindowsOptimizationRuntime runtime,
        OptimizationPlanDto plan,
        IProgress<AppProgressUpdate> progress,
        CancellationToken cancellationToken)
    {
        progress.Report(new AppProgressUpdate
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = AppProgressKind.Preparing,
            Percent = 71,
            Headline = localization.GetString("Runtime.WindowsConfirmation"),
            Detail = localization.GetString("Runtime.WindowsConfirmationDetail")
        });

        var adminProgress = new InlineProgress<AppProgressUpdate>(update => progress.Report(
            update.ActionId is null
                ? update
                : update with { Headline = GetLocalizedActionName(update.ActionId) }));

        ElevatedBrokerResult elevated;
        try
        {
            elevated = await brokerClient.ExecuteAsync(plan, adminProgress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var reason = localization.GetString("Runtime.AdminConfirmationCancelled");
            await runtime.Engine.MarkAdministratorPhaseFailedAsync(
                plan.PlanId,
                reason,
                CancellationToken.None).ConfigureAwait(false);
            return await CreateResultFromJournalAsync(
                plan.PlanId,
                plan.Profile,
                succeeded: false,
                wasCancelled: true,
                localization.GetString("Runtime.UacCancelledPreserved"),
                CancellationToken.None,
                failureBugCode: BugCode.APP_OPT_CANCELLED,
                failureErrorCategory: "cancelled").ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            var reason = localization.Format(
                "Runtime.BrokerResultUnconfirmed",
                localization.DescribeException(exception));
            await runtime.Engine.MarkAdministratorPhaseFailedAsync(
                plan.PlanId,
                reason,
                CancellationToken.None).ConfigureAwait(false);
            return await CreateResultFromJournalAsync(
                plan.PlanId,
                plan.Profile,
                succeeded: false,
                wasCancelled: false,
                localization.Format("Runtime.AdminPhaseFailedPreserved", reason),
                CancellationToken.None,
                failureBugCode: BugCodeClassifier.ClassifyBrokerException(exception),
                failureErrorCategory: TelemetryErrorClassifier.ClassifyException(exception)).ConfigureAwait(false);
        }

        if (!elevated.Succeeded)
        {
            // A falha (ou cancelamento do UAC) da fase administrativa não
            // é motivo para desfazer as ações de usuário padrão já
            // confirmadas -- isso é o que causava várias etapas
            // aparentemente "quebradas" quando só o plano de energia
            // falhava. Só a própria ação administrativa é marcada como
            // falha; o restante permanece Committed.
            await runtime.Engine.MarkAdministratorPhaseFailedAsync(
                plan.PlanId,
                elevated.Message,
                CancellationToken.None).ConfigureAwait(false);
            var summary = elevated.WasCancelled
                ? localization.GetString("Runtime.UacCancelledPreserved")
                : localization.Format("Runtime.AdminPhaseFailedPreserved", elevated.Message);

            return await CreateResultFromJournalAsync(
                plan.PlanId,
                plan.Profile,
                succeeded: false,
                wasCancelled: elevated.WasCancelled,
                summary,
                CancellationToken.None,
                failureBugCode: BugCodeClassifier.ClassifyBrokerFailure(
                    elevated.ErrorCode,
                    elevated.WasCancelled),
                failureErrorCategory: elevated.WasCancelled ? "cancelled" : "unexpected").ConfigureAwait(false);
        }

        return null;
    }

    private async Task<bool> LoadFinalRunSucceededAsync(
        OptimizationPlanDto plan,
        CancellationToken cancellationToken)
    {
        var finalJournal = await LoadJournalAsync(plan.PlanId, cancellationToken).ConfigureAwait(false);
        var finalReport = finalJournal is null
            ? null
            : OptimizationReportBuilder.Build(finalJournal, plan.Profile);
        return finalReport?.Succeeded ?? true;
    }

    private void ReportCompletion(IProgress<AppProgressUpdate> progress, bool runSucceeded)
    {
        progress.Report(new AppProgressUpdate
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = runSucceeded ? AppProgressKind.Completed : AppProgressKind.Warning,
            Percent = 100,
            Headline = localization.GetString(
                        runSucceeded ? "Runtime.PlanCompleted" : "Runtime.PlanCompletedWithErrors"),
            Detail = localization.GetString(
                        runSucceeded ? "Runtime.PlanCompletedDetail" : "Runtime.PlanCompletedWithErrorsDetail")
        });
    }

    private static string DetailKeyFor(ActionExecutionOutcome outcome) => outcome switch
    {
        ActionExecutionOutcome.Verified => "Runtime.ActionVerified",
        ActionExecutionOutcome.Applied => "Runtime.ActionCompleted",
        ActionExecutionOutcome.Skipped => "Runtime.ActionSkipped",
        ActionExecutionOutcome.Failed => "Runtime.ActionFailed",
        ActionExecutionOutcome.RolledBack => "Runtime.ActionRolledBack",
        _ => "Runtime.ApplyingAction"
    };

    private async Task<bool> RollbackCoreAsync(
        Guid transactionId,
        IProgress<AppProgressUpdate> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(new AppProgressUpdate
        {
            Timestamp = DateTimeOffset.UtcNow,
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
        if (localResult.State == TransactionState.RollbackFailed)
        {
            return HandleRollbackFailure(
                new WindowsFiveMProcessInspector(),
                localization,
                progress);
        }

        if (localResult.State is not (
            TransactionState.RolledBack
            or TransactionState.AwaitingElevationRollback))
        {
            progress.Report(new AppProgressUpdate
            {
                Timestamp = DateTimeOffset.UtcNow,
                Kind = AppProgressKind.Warning,
                Percent = 100,
                Headline = localization.GetString("Status.CouldNotRestore"),
                Detail = localization.GetString("Runtime.RestoreIncomplete")
            });
            return false;
        }

        if (localResult.State == TransactionState.AwaitingElevationRollback)
        {
            var elevated = await ExecuteElevatedRollbackAsync(
                transactionId,
                progress,
                cancellationToken).ConfigureAwait(false);
            if (!elevated)
            {
                return false;
            }
        }

        progress.Report(new AppProgressUpdate
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = AppProgressKind.Completed,
            Percent = 100,
            Headline = localization.GetString("Runtime.RestoreCompleted"),
            Detail = localization.GetString("Runtime.RestoreCompletedDetail")
        });
        return true;
    }

    /// <summary>
    /// Delega o rollback administrativo ao broker elevado. Retorna
    /// <see langword="false"/> quando o usuário cancela a confirmação do UAC
    /// (a transação permanece aguardando restauração) e lança quando o broker
    /// falha por outro motivo.
    /// </summary>
    private async Task<bool> ExecuteElevatedRollbackAsync(
        Guid transactionId,
        IProgress<AppProgressUpdate> progress,
        CancellationToken cancellationToken)
    {
        progress.Report(new AppProgressUpdate
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = AppProgressKind.RollingBack,
            Percent = 70,
            Headline = localization.GetString("Runtime.ConfirmRestore"),
            Detail = localization.GetString("Runtime.ConfirmRestoreDetail")
        });
        var elevated = await brokerClient.RollbackAsync(
            transactionId,
            progress,
            cancellationToken).ConfigureAwait(false);
        if (elevated.Succeeded)
        {
            return true;
        }

        if (elevated.WasCancelled)
        {
            progress.Report(new AppProgressUpdate
            {
                Timestamp = DateTimeOffset.UtcNow,
                Kind = AppProgressKind.Warning,
                Percent = 72,
                Headline = localization.GetString("Runtime.AdminRestorePending"),
                Detail = localization.GetString("Runtime.AdminRestorePendingDetail")
            });
            return false;
        }

        throw new InvalidOperationException(elevated.Message);
    }

    private WindowsOptimizationRuntime CreateRuntimeForDetectedInstallation()
    {
        var environment = WindowsOptimizationEnvironment.DetectDefault() with
        {
            JournalDirectory = journalDirectory
        };
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
            WindowsOptimizationDependencies.CreateDefault(environment, localization.Format));
    }

    internal WindowsOptimizationRuntime CreateRuntimeForPlan(OptimizationPlanDto plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Scope == OptimizationScope.GeneralWindows)
        {
            var environment = WindowsOptimizationEnvironment.DetectDefault() with
            {
                JournalDirectory = journalDirectory
            };
            return WindowsOptimizationRuntime.Create(
                environment,
                WindowsOptimizationDependencies.CreateDefault(environment, localization.Format));
        }

        if (plan.Scope != OptimizationScope.FiveMLegacy
            || string.IsNullOrWhiteSpace(detectedLegacyRoot))
        {
            throw new InvalidOperationException(
                "A detected FiveM Legacy installation is required for this optimization scope.");
        }

        return CreateRuntimeForDetectedInstallation();
    }

    internal static bool HandleRollbackFailure(
        IFiveMProcessInspector processInspector,
        ILocalizationService localization,
        IProgress<AppProgressUpdate> progress)
    {
        ArgumentNullException.ThrowIfNull(processInspector);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(progress);

        var blockReason = WindowsGamingControlsService.GetMutationBlockReason(processInspector);
        if (blockReason != WindowsGamingControlsBlockReason.None)
        {
            var detailKey = blockReason == WindowsGamingControlsBlockReason.FiveMRunning
                ? "Runtime.RestoreBlockedFiveM"
                : "Runtime.RestoreProcessCheckFailed";
            progress.Report(new AppProgressUpdate
            {
                Timestamp = DateTimeOffset.UtcNow,
                Kind = AppProgressKind.Warning,
                Percent = 100,
                Headline = localization.GetString(detailKey),
                Detail = localization.GetString(detailKey)
            });
            return false;
        }

        throw new InvalidOperationException(localization.GetString("Runtime.RollbackConflict"));
    }

    private async Task<AppOptimizationResult> CreateResultFromJournalAsync(
        Guid transactionId,
        OptimizationProfile profile,
        bool succeeded,
        bool wasCancelled,
        string summary,
        CancellationToken cancellationToken,
        BugCode? failureBugCode = null,
        string? failureErrorCategory = null)
    {
        var journal = await LoadJournalAsync(transactionId, cancellationToken).ConfigureAwait(false);
        return new AppOptimizationResult
        {
            TransactionId = transactionId,
            Succeeded = succeeded,
            WasCancelled = wasCancelled,
            Summary = summary,
            CompletedActions = journal?.Actions.Count(action =>
                action.State == ActionJournalState.Committed) ?? 0,
            BytesFreed = journal is null ? 0 : SumCommittedCleanupBytes(journal),
            Report = journal is null ? null : OptimizationReportBuilder.Build(journal, profile),
            FailureBugCode = failureBugCode,
            FailureErrorCategory = failureErrorCategory
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
                     entry.State == ActionJournalState.Committed
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
            {
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

    private static string GetArchitectureLabel() => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.X86 => "x86",
        Architecture.Arm64 => "ARM64",
        Architecture.Arm => "ARM",
        _ => RuntimeInformation.OSArchitecture.ToString()
    };

    private static string GetOperatingSystemLabel()
    {
        if (!OperatingSystem.IsWindows())
        {
            return RuntimeInformation.OSDescription;
        }

        return Environment.OSVersion.Version.Build >= 22000
            ? "Microsoft Windows 11"
            : "Microsoft Windows 10";
    }

    private static string? GetMemoryModuleLayout()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory");
            var modules = searcher.Get()
                .Cast<ManagementObject>()
                .Select(module => module["Capacity"])
                .OfType<ulong>()
                .Select(bytes => Math.Round(bytes / 1024d / 1024d / 1024d))
                .Where(size => size > 0)
                .GroupBy(size => size)
                .OrderByDescending(group => group.Key)
                .Select(group => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{group.Count()}×{group.Key:0} GB"))
                .ToArray();

            return modules.Length == 0 ? null : string.Join(" + ", modules);
        }
        catch (ManagementException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static OptimizationProfile InferProfile(WindowsTransactionJournal journal)
    {
        return journal.Actions.Any(action =>
                action.ActionId.Contains("aggressive", StringComparison.Ordinal)
                || action.ActionId == OptimizationActionIds.ReduceWindowsVisualEffects)
            ? OptimizationProfile.Aggressive
            : journal.Actions.Any(action => action.ActionId.Contains("balanced", StringComparison.Ordinal)
                || action.ActionId.Contains("background-capture", StringComparison.Ordinal)
                || action.ActionId.Contains("power", StringComparison.Ordinal))
                ? OptimizationProfile.Balanced
                : OptimizationProfile.Light;
    }

    private static bool IsWindowsGamingControlsTransaction(WindowsTransactionJournal journal)
    {
        return journal.Actions.Count == 2
            && journal.Actions.Select(action => action.ActionId).ToHashSet(StringComparer.Ordinal)
                .SetEquals(
                [
                    OptimizationActionIds.EnableGameMode,
                    OptimizationActionIds.DisableBackgroundCapture
                ]);
    }

    private string TranslateState(TransactionState state) => localization.GetString(state switch
    {
        TransactionState.Committed => "History.State.Committed",
        TransactionState.CommittedWithErrors => "History.State.CommittedWithErrors",
        TransactionState.AwaitingElevation => "History.State.AwaitingUac",
        TransactionState.AwaitingElevationRollback => "History.State.AdminRollbackPending",
        TransactionState.AwaitingStandardRollback => "History.State.LocalRollbackPending",
        TransactionState.RolledBack => "History.State.RolledBack",
        TransactionState.RollbackFailed => "History.State.RollbackFailed",
        TransactionState.Failed => "History.State.FailedSafely",
        _ => "History.State.Interrupted"
    });

    private static bool CanOfferRollback(WindowsActionJournalEntry action)
    {
        if (!action.Changed
            || string.IsNullOrWhiteSpace(action.SnapshotJson)
            || action.State is not (ActionJournalState.Committed
                or ActionJournalState.Failed
                or ActionJournalState.RollbackFailed)
            || !ActionCatalog.Current.TryGet(action.ActionId, out var definition)
            || definition!.Version != action.Version
            || action.Reversibility == ActionReversibility.Irreversible)
        {
            return false;
        }

        return action.Reversibility != ActionReversibility.RebuildableData
            || action.State == ActionJournalState.RollbackFailed
            || (action.State == ActionJournalState.Failed
                && action.RollbackSafeAfterInterruption);
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> callback;

        public InlineProgress(Action<T> callback)
        {
            this.callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public void Report(T value) => callback(value);
    }
}
