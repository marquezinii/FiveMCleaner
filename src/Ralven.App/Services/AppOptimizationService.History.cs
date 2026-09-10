using System.IO;
using System.Security;
using System.Text.Json;
using Ralven.Contracts;
using Ralven.Core.Catalog;
using Ralven.Windows.Engine;
using Microsoft.Win32;

namespace Ralven.App.Services;

/// <summary>
/// Leitura do histórico já gravado: journals, relatórios e a decisão sobre
/// quais transações ainda podem ser restauradas.
/// </summary>
public sealed partial class AppOptimizationService
{
    public Task<IReadOnlyList<AppHistoryRecord>> LoadHistoryAsync(
        CancellationToken cancellationToken = default) =>
        // Directory enumeration, metadata and receipt reads are synchronous even
        // when journal deserialization awaits; keep the entire scan off the UI.
        Task.Run(() => LoadHistoryCoreAsync(cancellationToken), cancellationToken);

    private async Task<IReadOnlyList<AppHistoryRecord>> LoadHistoryCoreAsync(
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
}
