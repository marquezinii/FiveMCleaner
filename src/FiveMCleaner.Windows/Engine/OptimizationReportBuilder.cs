using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;

namespace FiveMCleaner.Windows.Engine;

/// <summary>
/// Builds a structured <see cref="OptimizationReportDto"/> from a transaction
/// journal. Reads only local journal data; performs no system access.
/// </summary>
public static class OptimizationReportBuilder
{
    public static OptimizationReportDto Build(
        WindowsTransactionJournal journal,
        OptimizationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(journal);

        var lines = new List<OptimizationReportLineDto>(journal.Actions.Count);
        var requiresRestart = false;
        var restorePossible = false;

        foreach (var entry in journal.Actions.OrderBy(entry => entry.Sequence))
        {
            var outcome = ResolveOutcome(entry);
            var definition = ActionCatalog.Current.TryGet(entry.ActionId, out var found)
                ? found
                : null;

            lines.Add(new OptimizationReportLineDto
            {
                Sequence = entry.Sequence,
                ActionId = entry.ActionId,
                ActionName = definition?.Name ?? entry.ActionId,
                Category = definition?.Category ?? ActionCategory.Safety,
                Outcome = outcome,
                Reason = entry.OutcomeReason
            });

            if (outcome == ActionExecutionOutcome.Applied)
            {
                if (definition?.RequiresRestart == true)
                {
                    requiresRestart = true;
                }

                if (entry.Reversibility is not (
                    ActionReversibility.Irreversible or ActionReversibility.RebuildableData))
                {
                    restorePossible = true;
                }
            }
        }

        var failed = lines.Count(line => line.Outcome is
            ActionExecutionOutcome.Failed or ActionExecutionOutcome.RolledBack);
        var rollbackFailed = lines.Count(line => line.Outcome == ActionExecutionOutcome.RollbackFailed);

        return new OptimizationReportDto
        {
            TransactionId = journal.TransactionId,
            Profile = profile,
            CreatedAtUtc = journal.CreatedAtUtc,
            VerifiedCount = lines.Count(line => line.Outcome == ActionExecutionOutcome.Verified),
            ChangedCount = lines.Count(line => line.Outcome == ActionExecutionOutcome.Applied),
            SkippedCount = lines.Count(line => line.Outcome == ActionExecutionOutcome.Skipped),
            WarningCount = lines.Count(line => line.Outcome == ActionExecutionOutcome.Warning),
            FailedCount = failed,
            RollbackFailedCount = rollbackFailed,
            NotRunCount = lines.Count(line => line.Outcome == ActionExecutionOutcome.NotRun),
            RequiresRestart = requiresRestart,
            RestorePossible = restorePossible,
            Succeeded = failed == 0 && rollbackFailed == 0,
            Lines = lines
        };
    }

    private static ActionExecutionOutcome ResolveOutcome(WindowsActionJournalEntry entry)
    {
        if (entry.Outcome != ActionExecutionOutcome.Pending)
        {
            return entry.Outcome;
        }

        // Fallback for journals written before outcomes were recorded.
        return entry.State switch
        {
            WindowsActionJournalState.Committed => entry.Changed
                ? ActionExecutionOutcome.Applied
                : ActionExecutionOutcome.Verified,
            WindowsActionJournalState.RolledBack => ActionExecutionOutcome.RolledBack,
            WindowsActionJournalState.RollbackFailed => ActionExecutionOutcome.RollbackFailed,
            WindowsActionJournalState.Failed => ActionExecutionOutcome.Failed,
            WindowsActionJournalState.Skipped => ActionExecutionOutcome.Skipped,
            WindowsActionJournalState.Pending
                or WindowsActionJournalState.DeferredPrivilege
                or WindowsActionJournalState.SkippedPrivilege => ActionExecutionOutcome.NotRun,
            _ => ActionExecutionOutcome.Pending
        };
    }
}
