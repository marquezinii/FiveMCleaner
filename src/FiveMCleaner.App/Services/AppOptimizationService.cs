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
    private readonly string settingsPath;
    private readonly JsonSerializerOptions indentedJson;
    private readonly ElevatedBrokerClient brokerClient;
    private readonly bool demoMode;
    private string? detectedLegacyRoot;

    public AppOptimizationService(bool demoMode = false)
    {
        this.demoMode = demoMode;
        appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductIdentity.Name);
        journalDirectory = Path.Combine(appDataDirectory, "Transactions");
        settingsPath = Path.Combine(appDataDirectory, "settings.json");
        indentedJson = new JsonSerializerOptions(FiveMCleanerJson.Options) { WriteIndented = true };
        brokerClient = new ElevatedBrokerClient(appDataDirectory);
    }

    public string LogsDirectory => appDataDirectory;

    public async Task<AppDiagnostic> DiagnoseAsync(CancellationToken cancellationToken = default)
    {
        if (demoMode)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CreateDemoDiagnostic();
        }

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var installation = DetectFiveMInstallation();
            detectedLegacyRoot = installation.Edition == FiveMEdition.Legacy
                ? installation.Root
                : null;
            var memoryStatus = GetMemoryStatus();
            var systemDrive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            var cacheBytes = installation.Edition == FiveMEdition.Legacy && installation.Root is not null
                ? GetLegacyServerCacheBytes(installation.Root, cancellationToken)
                : 0L;
            var gpuName = GetGpuName();
            var memoryGiB = memoryStatus.TotalPhysical / 1024d / 1024d / 1024d;
            var freeDiskGiB = systemDrive.AvailableFreeSpace / 1024d / 1024d / 1024d;
            var running = IsFiveMRunning();

            var score = 15;
            score += memoryGiB >= 16 ? 25 : memoryGiB >= 8 ? 16 : 6;
            score += freeDiskGiB >= 25 ? 20 : freeDiskGiB >= 10 ? 11 : 3;
            score += installation.Edition == FiveMEdition.Legacy ? 25 : installation.Edition == FiveMEdition.Enhanced ? 10 : 0;
            score += cacheBytes < 8L * 1024 * 1024 * 1024 ? 15 : 6;
            score = Math.Clamp(score, 0, 100);

            var recommendation = memoryGiB <= 8 || freeDiskGiB < 12
                ? OptimizationProfile.Aggressive
                : memoryGiB >= 24 && freeDiskGiB >= 30
                    ? OptimizationProfile.Light
                    : OptimizationProfile.Balanced;
            var notices = new List<string>();
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
                CpuName = GetCpuName(),
                GpuName = gpuName,
                TotalMemoryGiB = memoryGiB,
                FreeDiskGiB = freeDiskGiB,
                LegacyCacheBytes = cacheBytes,
                OsLabel = RuntimeInformation.OSDescription,
                ReadinessScore = score,
                RecommendedProfile = recommendation,
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
            throw new InvalidOperationException(
                "O modo de demonstração usado nas capturas públicas nunca executa alterações no Windows.");
        }

        return ExecutePlanCoreAsync(plan, progress, cancellationToken);
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
                "O modo de demonstração não acessa o histórico real do computador.");
        }

        return RollbackCoreAsync(transactionId, progress, cancellationToken);
    }

    private static AppDiagnostic CreateDemoDiagnostic()
    {
        return new AppDiagnostic
        {
            Edition = FiveMEdition.Legacy,
            IsFiveMRunning = false,
            FiveMRoot = null,
            CpuName = "Processador de 6 núcleos",
            GpuName = "GPU dedicada • 8 GB",
            TotalMemoryGiB = 16,
            FreeDiskGiB = 128,
            LegacyCacheBytes = 3L * 1024 * 1024 * 1024,
            OsLabel = "Windows 11",
            ReadinessScore = 88,
            RecommendedProfile = OptimizationProfile.Balanced,
            Notices = ["Configuração equilibrada; o perfil médio preserva qualidade e prioriza consistência."]
        };
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
            Headline = "Validando o plano",
            Detail = "Conferindo versão, edição e ações permitidas."
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
                Headline = "Otimizando com segurança",
                Detail = update.Message,
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
                localResult.Error ?? "As alterações locais não foram confirmadas e foram revertidas.",
                cancellationToken).ConfigureAwait(false);
        }

        if (localResult.DeferredAdministratorActionIds.Count > 0)
        {
            progress.Report(new AppProgressUpdate
            {
                Timestamp = DateTimeOffset.Now,
                Kind = AppProgressKind.Preparing,
                Percent = 71,
                Headline = "Confirmação do Windows necessária",
                Detail = "O broker limitado solicitará UAC somente para o plano de energia allowlisted."
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
                        "A confirmação administrativa foi cancelada antes de começar.",
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
                        $"O componente administrativo não confirmou o resultado: {exception.Message}",
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
                    ? "UAC cancelado. Ajustes reversíveis já aplicados foram restaurados; limpezas concluídas permanecem registradas."
                    : $"A fase administrativa falhou com segurança: {elevated.Message}";
                if (rollback.State == WindowsTransactionState.RollbackFailed)
                {
                    summary += " O journal preservou um erro de rollback para diagnóstico.";
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
            Headline = "Plano concluído",
            Detail = "Alterações verificadas e registradas para rollback."
        });
        return await CreateResultFromJournalAsync(
            plan.PlanId,
            succeeded: true,
            wasCancelled: false,
            "Otimização concluída e registrada no histórico local.",
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
            Headline = "Preparando restauração",
            Detail = $"Validando transação {transactionId:N}."
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
            throw new InvalidOperationException(
                "O rollback local encontrou um conflito e preservou o estado mais recente do usuário.");
        }

        if (localResult.State == WindowsTransactionState.AwaitingElevationRollback)
        {
            progress.Report(new AppProgressUpdate
            {
                Timestamp = DateTimeOffset.Now,
                Kind = AppProgressKind.RollingBack,
                Percent = 70,
                Headline = "Confirme a restauração no Windows",
                Detail = "O plano de energia anterior requer o broker elevado para ser restaurado."
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
                        Headline = "Restauração administrativa pendente",
                        Detail = "Confirme o UAC pelo histórico quando quiser concluir o rollback."
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
            Headline = "Configurações restauradas",
            Detail = "O rollback respeitou mudanças mais recentes e não tentou recuperar limpezas permanentes."
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
                environment = environment with
                {
                    FiveMInstallationRoot = fullRoot,
                    FiveMAppRoot = appRoot,
                    FiveMExecutablePath = executable
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

    private static string DescribeInterruptedBroker(
        string reason,
        WindowsTransactionResult? rollback)
    {
        return rollback?.State switch
        {
            WindowsTransactionState.RolledBack =>
                $"{reason} Ajustes reversíveis locais foram restaurados; limpezas já confirmadas permanecem no histórico.",
            WindowsTransactionState.AwaitingElevationRollback =>
                $"{reason} O histórico indica uma restauração administrativa pendente; use Desfazer para concluí-la com UAC.",
            WindowsTransactionState.RollbackFailed =>
                $"{reason} O rollback local encontrou um conflito e o journal foi preservado para diagnóstico.",
            null =>
                $"{reason} O estado final não pôde ser confirmado; consulte o journal antes de tentar novamente.",
            _ =>
                $"{reason} Consulte o histórico local para confirmar o estado final da transação."
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
        return Process.GetProcesses().Any(process =>
        {
            try
            {
                return WindowsFiveMProcessInspector.LooksLikeFiveMProcessName(
                    process.ProcessName);
            }
            finally
            {
                process.Dispose();
            }
        });
    }

    private static string GetCpuName()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "Processador não identificado";
    }

    private static string GetGpuName()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var video = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Video");
            if (video is null)
            {
                return "GPU detectada pelo Windows";
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

        return names.Count > 0
            ? string.Join(" / ", names.Order(StringComparer.OrdinalIgnoreCase))
            : "GPU detectada pelo Windows";
    }

    private static MemoryStatusEx GetMemoryStatus()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status))
        {
            throw new InvalidOperationException("O Windows não retornou o estado da memória física.");
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

    private static string TranslateState(WindowsTransactionState state) => state switch
    {
        WindowsTransactionState.Committed => "Concluído",
        WindowsTransactionState.AwaitingElevation => "Aguardando confirmação UAC",
        WindowsTransactionState.AwaitingElevationRollback => "Rollback administrativo pendente",
        WindowsTransactionState.AwaitingStandardRollback => "Rollback local pendente",
        WindowsTransactionState.RolledBack => "Restaurado",
        WindowsTransactionState.RollbackFailed => "Rollback com erro",
        WindowsTransactionState.Failed => "Falhou com segurança",
        _ => "Interrompido"
    };

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
