using FiveMCleaner.Contracts;
using FiveMCleaner.Windows.Actions;

namespace FiveMCleaner.Windows.Engine;

public sealed record WindowsTransactionOptions
{
    public bool IncludeStandardUserActions { get; init; } = true;

    public bool IncludeAdministratorActions { get; init; } = true;

    /// <summary>
    /// Strict mode only: when a genuine failure occurs, roll back every action
    /// already applied in this run and mark the whole transaction failed.
    /// Ignored when <see cref="IsolateFailures"/> is true.
    /// </summary>
    public bool RollbackOnFailure { get; init; } = true;

    /// <summary>
    /// When true, each action is executed as an isolated mini-transaction:
    /// verify → apply → commit, rolling back only itself on failure while
    /// unrelated safe actions keep running. Actions whose prerequisite did not
    /// succeed are skipped; a failed critical action aborts the remaining run.
    /// </summary>
    public bool IsolateFailures { get; init; }
}

public sealed record WindowsRollbackOptions
{
    public bool IncludeStandardUserActions { get; init; } = true;

    public bool IncludeAdministratorActions { get; init; } = true;
}

public sealed record WindowsTransactionResult
{
    public required Guid TransactionId { get; init; }

    public required TransactionState State { get; init; }

    public required IReadOnlyList<string> AppliedActionIds { get; init; }

    public required IReadOnlyList<string> DeferredAdministratorActionIds { get; init; }

    public string? Error { get; init; }
}

public sealed class WindowsTransactionEngine
{
    private readonly WindowsActionCatalog catalog;
    private readonly IWindowsTransactionJournalStore journalStore;
    private readonly SemaphoreSlim executionGate = new(1, 1);

    public WindowsTransactionEngine(
        WindowsActionCatalog catalog,
        IWindowsTransactionJournalStore journalStore)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.journalStore = journalStore ?? throw new ArgumentNullException(nameof(journalStore));
    }

    public async Task<WindowsTransactionResult> ExecuteAsync(
        IEnumerable<IWindowsOptimizationAction> requestedActions,
        WindowsActionContext context,
        WindowsTransactionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new WindowsTransactionOptions();
        var actions = ValidateExecutionRequest(requestedActions, context);

        await executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var journal = await LoadOrCreateJournalAsync(actions, context, cancellationToken)
                .ConfigureAwait(false);
            var selected = BuildSelectedActions(actions, journal, context, options);

            if (selected.Count == 0)
            {
                return await CompleteWithNoSelectedActionsAsync(journal, cancellationToken)
                    .ConfigureAwait(false);
            }

            journal.State = TransactionState.Applying;
            await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);

            if (options.IsolateFailures)
            {
                return await ExecuteIsolatedAsync(journal, selected, context, cancellationToken)
                    .ConfigureAwait(false);
            }

            var applied = new List<(IWindowsOptimizationAction Action, WindowsActionJournalEntry Entry)>();
            try
            {
                await ApplyAndCommitAsync(journal, selected, applied, context, cancellationToken)
                    .ConfigureAwait(false);
                return CreateResult(
                    journal,
                    applied.Select(item => item.Action.Metadata.Id).ToArray(),
                    GetDeferredAdministratorIds(journal),
                    null);
            }
            catch (Exception exception) when (exception is not StackOverflowException)
            {
                return await HandleExecutionFailureAsync(
                    journal,
                    applied,
                    options,
                    context,
                    exception).ConfigureAwait(false);
            }
        }
        finally
        {
            executionGate.Release();
        }
    }

    private IWindowsOptimizationAction[] ValidateExecutionRequest(
        IEnumerable<IWindowsOptimizationAction> requestedActions,
        WindowsActionContext context)
    {
        ArgumentNullException.ThrowIfNull(requestedActions);
        ArgumentNullException.ThrowIfNull(context);
        if (context.TransactionId == Guid.Empty)
        {
            throw new ArgumentException("TransactionId cannot be empty.", nameof(context));
        }

        var actions = requestedActions.ToArray();
        if (actions.Length == 0)
        {
            throw new ArgumentException("At least one action is required.", nameof(requestedActions));
        }

        foreach (var action in actions)
        {
            catalog.Validate(action);
        }

        if (actions.Select(action => action.Metadata.Id).Distinct(StringComparer.Ordinal).Count()
            != actions.Length)
        {
            throw new ArgumentException("A transaction cannot contain duplicate action IDs.", nameof(requestedActions));
        }

        return actions;
    }

    private async Task<WindowsTransactionJournal> LoadOrCreateJournalAsync(
        IReadOnlyList<IWindowsOptimizationAction> actions,
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        var journal = await journalStore.LoadAsync(context.TransactionId, cancellationToken)
            .ConfigureAwait(false);
        if (journal is null)
        {
            journal = CreateJournal(actions, context);
            await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            ValidateExistingJournal(journal, actions);
            journal.WasElevated |= context.IsElevated;
        }

        return journal;
    }

    private static IReadOnlyList<(IWindowsOptimizationAction Action, WindowsActionJournalEntry Entry)>
        BuildSelectedActions(
            IReadOnlyList<IWindowsOptimizationAction> actions,
            WindowsTransactionJournal journal,
            WindowsActionContext context,
            WindowsTransactionOptions options)
    {
        var entriesById = journal.Actions.ToDictionary(
            entry => entry.ActionId,
            StringComparer.Ordinal);
        var selected = new List<(IWindowsOptimizationAction Action, WindowsActionJournalEntry Entry)>();
        foreach (var action in actions)
        {
            var entry = entriesById[action.Metadata.Id];
            if (entry.State == ActionJournalState.SkippedPrivilege)
            {
                entry.State = ActionJournalState.DeferredPrivilege;
            }

            if (entry.State == ActionJournalState.Committed)
            {
                continue;
            }

            if (entry.State is not (ActionJournalState.Pending
                or ActionJournalState.DeferredPrivilege))
            {
                throw new InvalidOperationException(
                    $"Action '{entry.ActionId}' cannot resume from state '{entry.State}'.");
            }

            var isAdministratorAction =
                action.Metadata.RequiredPrivilege == RequiredPrivilege.Administrator;
            // An action opted into "attempt without elevation first"
            // still gets a shot in the standard-user phase even though
            // it is otherwise gated on IsElevated -- most Windows
            // configurations allow it, and only a genuine access-denied
            // result (see the isolated-execution catch below) defers it
            // to the elevated broker phase.
            var attemptWithoutElevationFirst = isAdministratorAction
                && action.Metadata.AttemptWithoutElevationFirst
                && !context.IsElevated;
            var include = isAdministratorAction
                ? (options.IncludeAdministratorActions && context.IsElevated)
                    || (attemptWithoutElevationFirst && options.IncludeStandardUserActions)
                : options.IncludeStandardUserActions;
            if (!include)
            {
                if (isAdministratorAction)
                {
                    entry.State = ActionJournalState.DeferredPrivilege;
                }

                continue;
            }

            selected.Add((action, entry));
        }

        return selected;
    }

    private async Task<WindowsTransactionResult> CompleteWithNoSelectedActionsAsync(
        WindowsTransactionJournal journal,
        CancellationToken cancellationToken)
    {
        journal.State = DetermineSuccessfulState(journal);
        await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
        return CreateResult(journal, [], GetDeferredAdministratorIds(journal), null);
    }

    private async Task ApplyAndCommitAsync(
        WindowsTransactionJournal journal,
        IReadOnlyList<(IWindowsOptimizationAction Action, WindowsActionJournalEntry Entry)> selected,
        List<(IWindowsOptimizationAction Action, WindowsActionJournalEntry Entry)> applied,
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        var totalWeight = selected.Sum(item => Math.Max(1, item.Action.Metadata.ProgressWeight));
        var completedWeight = 0;

        foreach (var item in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            item.Entry.State = ActionJournalState.Applying;
            item.Entry.StartedAtUtc = DateTimeOffset.UtcNow;
            item.Entry.Error = null;
            await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);

            context.Progress?.Report(new WindowsActionProgress(
                context.TransactionId,
                item.Action.Metadata.Id,
                $"Aplicando {item.Action.Metadata.Name}",
                completedWeight,
                totalWeight));

            WindowsActionApplyResult result;
            try
            {
                result = await item.Action.ApplyAsync(context, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException) when (
                !context.IsElevated
                && item.Action.Metadata.RequiredPrivilege == RequiredPrivilege.Administrator
                && item.Action.Metadata.AttemptWithoutElevationFirst)
            {
                // Same "defer instead of fail" handling as the
                // isolated-execution path (see ExecuteIsolatedAsync).
                item.Entry.State = ActionJournalState.DeferredPrivilege;
                item.Entry.StartedAtUtc = null;
                item.Entry.Error = null;
                await journalStore.SaveAsync(journal, CancellationToken.None).ConfigureAwait(false);
                continue;
            }

            item.Entry.Changed = result.Changed;
            item.Entry.SnapshotJson = result.SnapshotJson;
            item.Entry.Messages.AddRange(result.Messages);
            item.Entry.State = ActionJournalState.Applied;
            item.Entry.CompletedAtUtc = DateTimeOffset.UtcNow;
            applied.Add(item);
            completedWeight += Math.Max(1, item.Action.Metadata.ProgressWeight);
            await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);

            context.Progress?.Report(new WindowsActionProgress(
                context.TransactionId,
                item.Action.Metadata.Id,
                $"Concluído: {item.Action.Metadata.Name}",
                completedWeight,
                totalWeight));
        }

        journal.State = TransactionState.Committing;
        await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);

        foreach (var item in OrderForCommit(applied))
        {
            cancellationToken.ThrowIfCancellationRequested();
            item.Entry.State = ActionJournalState.Committing;
            await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
            await item.Action.CommitAsync(
                context,
                item.Entry.SnapshotJson,
                cancellationToken).ConfigureAwait(false);
            item.Entry.State = ActionJournalState.Committed;
            item.Entry.Outcome = item.Entry.Changed
                ? ActionExecutionOutcome.Applied
                : ActionExecutionOutcome.Verified;
            item.Entry.CompletedAtUtc = DateTimeOffset.UtcNow;
            await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
        }

        journal.State = DetermineSuccessfulState(journal);
        journal.Error = null;
        await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
    }

    private async Task<WindowsTransactionResult> HandleExecutionFailureAsync(
        WindowsTransactionJournal journal,
        IReadOnlyList<(IWindowsOptimizationAction Action, WindowsActionJournalEntry Entry)> applied,
        WindowsTransactionOptions options,
        WindowsActionContext context,
        Exception exception)
    {
        journal.Error = exception.ToString();
        journal.State = TransactionState.Failed;
        var current = journal.Actions.LastOrDefault(entry =>
            entry.State is ActionJournalState.Applying
                or ActionJournalState.Committing);
        if (current is not null)
        {
            current.State = ActionJournalState.Failed;
            current.Outcome = ActionExecutionOutcome.Failed;
            current.Error = exception.ToString();
            current.CompletedAtUtc = DateTimeOffset.UtcNow;
        }

        await journalStore.SaveAsync(journal, CancellationToken.None).ConfigureAwait(false);
        if (options.RollbackOnFailure)
        {
            var rollbackCandidates = applied
                .Where(item => CanRollback(item.Entry))
                .ToArray();
            await RollbackAppliedAsync(
                journal,
                rollbackCandidates,
                context,
                TransactionState.RolledBack,
                CancellationToken.None).ConfigureAwait(false);
        }

        return CreateResult(
            journal,
            applied.Select(item => item.Action.Metadata.Id).ToArray(),
            GetDeferredAdministratorIds(journal),
            exception.Message);
    }

    public async Task<WindowsTransactionResult> RollbackAsync(
        Guid transactionId,
        bool isElevated,
        WindowsRollbackOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (transactionId == Guid.Empty)
        {
            throw new ArgumentException("Transaction ID cannot be empty.", nameof(transactionId));
        }

        options ??= new WindowsRollbackOptions();
        await executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var journal = await journalStore.LoadAsync(transactionId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new FileNotFoundException($"Transaction journal '{transactionId}' was not found.");
            ValidateJournalForRollback(journal, transactionId);
            journal.WasElevated |= isElevated;
            var context = new WindowsActionContext
            {
                TransactionId = transactionId,
                StartedAtUtc = DateTimeOffset.UtcNow,
                IsElevated = isElevated
            };

            var rollback = new List<(IWindowsOptimizationAction Action, WindowsActionJournalEntry Entry)>();
            var deferredAdministratorIds = new List<string>();
            foreach (var entry in journal.Actions)
            {
                if (!CanRollback(entry))
                {
                    continue;
                }

                if (entry.RequiredPrivilege == RequiredPrivilege.Administrator
                    && (!options.IncludeAdministratorActions || !isElevated))
                {
                    deferredAdministratorIds.Add(entry.ActionId);
                    continue;
                }

                if (entry.RequiredPrivilege == RequiredPrivilege.StandardUser
                    && !options.IncludeStandardUserActions)
                {
                    continue;
                }

                rollback.Add((catalog.GetRequired(entry.ActionId, entry.Version), entry));
            }

            var selectedIds = rollback
                .Select(item => item.Entry.ActionId)
                .ToHashSet(StringComparer.Ordinal);
            var hasRemainingStandardActions = journal.Actions.Any(entry =>
                CanRollback(entry)
                && entry.RequiredPrivilege == RequiredPrivilege.StandardUser
                && !selectedIds.Contains(entry.ActionId));
            var successState = deferredAdministratorIds.Count > 0
                ? TransactionState.AwaitingElevationRollback
                : hasRemainingStandardActions
                    ? TransactionState.AwaitingStandardRollback
                    : TransactionState.RolledBack;
            await RollbackAppliedAsync(
                journal,
                rollback,
                context,
                successState,
                cancellationToken).ConfigureAwait(false);

            return CreateResult(
                journal,
                rollback.Select(item => item.Action.Metadata.Id).ToArray(),
                deferredAdministratorIds,
                journal.State == TransactionState.RollbackFailed ? journal.Error : null);
        }
        finally
        {
            executionGate.Release();
        }
    }

    /// <summary>
    /// Marks every still-pending/deferred administrator action as failed
    /// without touching any already-committed standard-user action --
    /// used when the elevated broker phase itself fails or is cancelled, so
    /// a non-critical administrative failure (e.g. the high-performance
    /// power plan) never rolls back independent changes that already
    /// succeeded. The transaction settles as
    /// <see cref="TransactionState.CommittedWithErrors"/> instead of
    /// being torn down.
    /// </summary>
    public async Task<WindowsTransactionResult> MarkAdministratorPhaseFailedAsync(
        Guid transactionId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        await executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var journal = await journalStore.LoadAsync(transactionId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new FileNotFoundException($"Transaction journal '{transactionId}' was not found.");

            foreach (var entry in journal.Actions.Where(entry =>
                         entry.RequiredPrivilege == RequiredPrivilege.Administrator
                         && entry.State is ActionJournalState.Pending
                             or ActionJournalState.DeferredPrivilege))
            {
                entry.State = ActionJournalState.Failed;
                entry.Outcome = ActionExecutionOutcome.Failed;
                entry.OutcomeReason = reason;
                entry.Error = reason;
                entry.CompletedAtUtc = DateTimeOffset.UtcNow;
            }

            journal.State = TransactionState.CommittedWithErrors;
            journal.Error = reason;
            await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);

            return CreateResult(journal, [], [], reason);
        }
        finally
        {
            executionGate.Release();
        }
    }

    private static IReadOnlyList<(IWindowsOptimizationAction Action, WindowsActionJournalEntry Entry)>
        OrderForCommit(
            IReadOnlyList<(IWindowsOptimizationAction Action, WindowsActionJournalEntry Entry)> applied)
    {
        return applied
            .OrderBy(item => item.Action.Metadata.Reversibility is
                ActionReversibility.Irreversible or ActionReversibility.RebuildableData
                ? 1
                : 0)
            .ThenBy(item => item.Entry.Sequence)
            .ToArray();
    }

    private static bool CanRollback(WindowsActionJournalEntry entry)
    {
        if (!entry.Changed
            || string.IsNullOrWhiteSpace(entry.SnapshotJson)
            || entry.State is ActionJournalState.RolledBack
                or ActionJournalState.Pending
                or ActionJournalState.DeferredPrivilege
                or ActionJournalState.SkippedPrivilege
                or ActionJournalState.Skipped
                or ActionJournalState.Failed)
        {
            return false;
        }

        if (entry.State == ActionJournalState.Committed
            && entry.Reversibility is ActionReversibility.Irreversible
                or ActionReversibility.RebuildableData)
        {
            return false;
        }

        return true;
    }

    private void ValidateJournalForRollback(
        WindowsTransactionJournal journal,
        Guid requestedTransactionId)
    {
        if (journal.TransactionId != requestedTransactionId)
        {
            throw new InvalidDataException("The transaction journal ID does not match its requested file.");
        }

        if (journal.Actions.Count == 0
            || journal.Actions.Select(entry => entry.ActionId)
                .Distinct(StringComparer.Ordinal).Count() != journal.Actions.Count)
        {
            throw new InvalidDataException("The transaction journal action list is invalid.");
        }

        for (var index = 0; index < journal.Actions.Count; index++)
        {
            var entry = journal.Actions[index];
            if (entry.Sequence != index + 1)
            {
                throw new InvalidDataException("The transaction journal action order is invalid.");
            }

            var registered = catalog.GetRequired(entry.ActionId, entry.Version);
            if (entry.RequiredPrivilege != registered.Metadata.RequiredPrivilege
                || entry.Reversibility != registered.Metadata.Reversibility)
            {
                throw new InvalidDataException(
                    $"Journal metadata for action '{entry.ActionId}' does not match the allowlist.");
            }
        }
    }

    private static WindowsTransactionJournal CreateJournal(
        IReadOnlyList<IWindowsOptimizationAction> actions,
        WindowsActionContext context)
    {
        return new WindowsTransactionJournal
        {
            TransactionId = context.TransactionId,
            SchemaVersion = 1,
            CreatedAtUtc = context.StartedAtUtc,
            UpdatedAtUtc = context.StartedAtUtc,
            WasElevated = context.IsElevated,
            State = TransactionState.Created,
            Actions = actions.Select((action, index) => new WindowsActionJournalEntry
            {
                Sequence = index + 1,
                ActionId = action.Metadata.Id,
                Version = action.Metadata.Version,
                RequiredPrivilege = action.Metadata.RequiredPrivilege,
                Reversibility = action.Metadata.Reversibility,
                State = ActionJournalState.Pending
            }).ToList()
        };
    }

    private static void ValidateExistingJournal(
        WindowsTransactionJournal journal,
        IReadOnlyList<IWindowsOptimizationAction> requestedActions)
    {
        if (journal.State is TransactionState.Failed
            or TransactionState.RollbackFailed
            or TransactionState.RollingBack
            or TransactionState.RolledBack
            or TransactionState.AwaitingElevationRollback
            or TransactionState.AwaitingStandardRollback)
        {
            throw new InvalidOperationException(
                $"Transaction '{journal.TransactionId}' cannot resume from state '{journal.State}'.");
        }

        if (journal.Actions.Select(entry => entry.ActionId)
            .Distinct(StringComparer.Ordinal).Count() != journal.Actions.Count)
        {
            throw new InvalidOperationException("The existing journal contains duplicate action IDs.");
        }

        var entries = journal.Actions.ToDictionary(entry => entry.ActionId, StringComparer.Ordinal);
        foreach (var action in requestedActions)
        {
            if (!entries.TryGetValue(action.Metadata.Id, out var entry))
            {
                throw new InvalidOperationException(
                    $"Action '{action.Metadata.Id}' was not initialized in this transaction.");
            }

            if (entry.Version != action.Metadata.Version
                || entry.RequiredPrivilege != action.Metadata.RequiredPrivilege
                || entry.Reversibility != action.Metadata.Reversibility)
            {
                throw new InvalidOperationException(
                    $"Action '{action.Metadata.Id}' does not match its initialized journal entry.");
            }
        }
    }

    private static TransactionState DetermineSuccessfulState(
        WindowsTransactionJournal journal)
    {
        return journal.Actions.Any(entry => entry.State is
            ActionJournalState.Pending or ActionJournalState.DeferredPrivilege)
            ? TransactionState.AwaitingElevation
            : TransactionState.Committed;
    }

    private static IReadOnlyList<string> GetDeferredAdministratorIds(
        WindowsTransactionJournal journal)
    {
        return journal.Actions
            .Where(entry => entry.RequiredPrivilege == RequiredPrivilege.Administrator)
            .Where(entry => entry.State is
                ActionJournalState.Pending or ActionJournalState.DeferredPrivilege)
            .OrderBy(entry => entry.Sequence)
            .Select(entry => entry.ActionId)
            .ToArray();
    }

    private static WindowsTransactionResult CreateResult(
        WindowsTransactionJournal journal,
        IReadOnlyList<string> appliedActionIds,
        IReadOnlyList<string> deferredAdministratorActionIds,
        string? error)
    {
        return new WindowsTransactionResult
        {
            TransactionId = journal.TransactionId,
            State = journal.State,
            AppliedActionIds = appliedActionIds,
            DeferredAdministratorActionIds = deferredAdministratorActionIds,
            Error = error
        };
    }

    private async Task<WindowsTransactionResult> ExecuteIsolatedAsync(
        WindowsTransactionJournal journal,
        IReadOnlyList<(IWindowsOptimizationAction Action, WindowsActionJournalEntry Entry)> selected,
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        var entriesById = journal.Actions.ToDictionary(entry => entry.ActionId, StringComparer.Ordinal);
        var applied = new List<string>();
        var totalWeight = selected.Sum(item => Math.Max(1, item.Action.Metadata.ProgressWeight));
        var totalSteps = selected.Count;
        var completedWeight = 0;
        var step = 0;
        var aborted = false;

        foreach (var item in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            step++;
            var weight = Math.Max(1, item.Action.Metadata.ProgressWeight);

            if (aborted)
            {
                completedWeight += weight;
                await RecordSkippedActionAsync(
                    journal, item, context, step, totalSteps, completedWeight, totalWeight,
                    ActionExecutionOutcome.NotRun, "Ignorada após uma falha crítica anterior.")
                    .ConfigureAwait(false);
                continue;
            }

            var unmet = FindUnmetPrerequisite(item.Action.Metadata, entriesById);
            if (unmet is not null)
            {
                completedWeight += weight;
                await RecordSkippedActionAsync(
                    journal, item, context, step, totalSteps, completedWeight, totalWeight,
                    ActionExecutionOutcome.Skipped, $"Pré-requisito não atendido: {unmet}.")
                    .ConfigureAwait(false);
                continue;
            }

            await BeginItemApplicationAsync(
                journal, item, context, step, totalSteps, completedWeight, totalWeight,
                cancellationToken).ConfigureAwait(false);
            var result = await ApplyIsolatedItemAsync(journal, item, context, applied, cancellationToken)
                .ConfigureAwait(false);

            completedWeight += weight;
            if (result == IsolatedItemResult.FailedCritical)
            {
                aborted = true;
            }

            ReportStep(context, item, step, totalSteps, completedWeight, totalWeight,
                ReportOutcomeFor(result));
        }

        if (aborted)
        {
            AbortRemainingEntries(journal);
        }

        journal.State = DetermineIsolatedFinalState(journal);
        journal.Error = journal.Actions.Any(entry => entry.Outcome is
            ActionExecutionOutcome.Failed
            or ActionExecutionOutcome.RolledBack
            or ActionExecutionOutcome.RollbackFailed)
            ? "Uma ou mais ações não foram concluídas; consulte o relatório."
            : null;
        await journalStore.SaveAsync(journal, CancellationToken.None).ConfigureAwait(false);

        return CreateResult(journal, applied, GetDeferredAdministratorIds(journal), journal.Error);
    }

    private async Task RecordSkippedActionAsync(
        WindowsTransactionJournal journal,
        (IWindowsOptimizationAction Action, WindowsActionJournalEntry Entry) item,
        WindowsActionContext context,
        int step,
        int totalSteps,
        int completedWeight,
        int totalWeight,
        ActionExecutionOutcome outcome,
        string reason)
    {
        MarkTerminal(item.Entry, ActionJournalState.Skipped, outcome, reason);
        await journalStore.SaveAsync(journal, CancellationToken.None).ConfigureAwait(false);
        ReportStep(context, item, step, totalSteps, completedWeight, totalWeight, outcome);
    }

    private async Task BeginItemApplicationAsync(
        WindowsTransactionJournal journal,
        (IWindowsOptimizationAction Action, WindowsActionJournalEntry Entry) item,
        WindowsActionContext context,
        int step,
        int totalSteps,
        int completedWeight,
        int totalWeight,
        CancellationToken cancellationToken)
    {
        item.Entry.State = ActionJournalState.Applying;
        item.Entry.StartedAtUtc = DateTimeOffset.UtcNow;
        item.Entry.Error = null;
        await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
        ReportStep(context, item, step, totalSteps, completedWeight, totalWeight,
            ActionExecutionOutcome.Pending);
    }

    private async Task<IsolatedItemResult> ApplyIsolatedItemAsync(
        WindowsTransactionJournal journal,
        (IWindowsOptimizationAction Action, WindowsActionJournalEntry Entry) item,
        WindowsActionContext context,
        List<string> applied,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await item.Action.ApplyAsync(context, cancellationToken)
                .ConfigureAwait(false);
            item.Entry.Changed = result.Changed;
            item.Entry.SnapshotJson = result.SnapshotJson;
            item.Entry.Messages.AddRange(result.Messages);

            if (result.Changed)
            {
                item.Entry.State = ActionJournalState.Committing;
                await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
                await item.Action.CommitAsync(context, item.Entry.SnapshotJson, cancellationToken)
                    .ConfigureAwait(false);
                item.Entry.State = ActionJournalState.Committed;
                item.Entry.Outcome = ActionExecutionOutcome.Applied;
                applied.Add(item.Action.Metadata.Id);
            }
            else
            {
                item.Entry.State = ActionJournalState.Committed;
                item.Entry.Outcome = ActionExecutionOutcome.Verified;
            }

            item.Entry.CompletedAtUtc = DateTimeOffset.UtcNow;
            await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
            return item.Entry.Changed ? IsolatedItemResult.Applied : IsolatedItemResult.Verified;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await IsolatedRollbackSelfAsync(journal, item, context).ConfigureAwait(false);
            await FinalizeCancelledIsolatedRunAsync(journal).ConfigureAwait(false);
            throw;
        }
        catch (UnauthorizedAccessException) when (
            !context.IsElevated
            && item.Action.Metadata.RequiredPrivilege == RequiredPrivilege.Administrator
            && item.Action.Metadata.AttemptWithoutElevationFirst)
        {
            // This computer genuinely requires elevation for this
            // action -- not a failure, just defer it back to the
            // broker phase exactly as if it had never been attempted.
            item.Entry.State = ActionJournalState.DeferredPrivilege;
            item.Entry.Error = null;
            item.Entry.CompletedAtUtc = null;
            item.Entry.StartedAtUtc = null;
            await journalStore.SaveAsync(journal, CancellationToken.None).ConfigureAwait(false);
            return IsolatedItemResult.Deferred;
        }
        catch (Exception exception) when (exception is not StackOverflowException)
        {
            item.Entry.Error = exception.ToString();
            item.Entry.State = ActionJournalState.Failed;
            item.Entry.Outcome = ActionExecutionOutcome.Failed;
            item.Entry.CompletedAtUtc = DateTimeOffset.UtcNow;
            await journalStore.SaveAsync(journal, CancellationToken.None).ConfigureAwait(false);

            await IsolatedRollbackSelfAsync(journal, item, context).ConfigureAwait(false);
            return item.Action.Metadata.IsCritical
                ? IsolatedItemResult.FailedCritical
                : IsolatedItemResult.Failed;
        }
    }

    private void AbortRemainingEntries(WindowsTransactionJournal journal)
    {
        foreach (var entry in journal.Actions.Where(entry =>
                     entry.State is ActionJournalState.Pending
                         or ActionJournalState.DeferredPrivilege))
        {
            MarkTerminal(entry, ActionJournalState.Skipped,
                ActionExecutionOutcome.NotRun, "Ignorada após uma falha crítica anterior.");
        }
    }

    private static ActionExecutionOutcome ReportOutcomeFor(IsolatedItemResult result)
    {
        return result switch
        {
            IsolatedItemResult.Applied => ActionExecutionOutcome.Applied,
            IsolatedItemResult.Verified => ActionExecutionOutcome.Verified,
            IsolatedItemResult.Deferred => ActionExecutionOutcome.Skipped,
            _ => ActionExecutionOutcome.Failed
        };
    }

    private enum IsolatedItemResult
    {
        Applied,
        Verified,
        Deferred,
        Failed,
        FailedCritical
    }

    private async Task IsolatedRollbackSelfAsync(
        WindowsTransactionJournal journal,
        (IWindowsOptimizationAction Action, WindowsActionJournalEntry Entry) item,
        WindowsActionContext context)
    {
        if (!item.Entry.Changed || string.IsNullOrWhiteSpace(item.Entry.SnapshotJson))
        {
            return;
        }

        try
        {
            item.Entry.State = ActionJournalState.RollingBack;
            await journalStore.SaveAsync(journal, CancellationToken.None).ConfigureAwait(false);
            await item.Action.RollbackAsync(context, item.Entry.SnapshotJson, CancellationToken.None)
                .ConfigureAwait(false);
            item.Entry.State = ActionJournalState.RolledBack;
            item.Entry.Outcome = ActionExecutionOutcome.RolledBack;
        }
        catch (Exception exception) when (exception is not StackOverflowException)
        {
            item.Entry.State = ActionJournalState.RollbackFailed;
            item.Entry.Outcome = ActionExecutionOutcome.RollbackFailed;
            item.Entry.Error = exception.ToString();
        }

        await journalStore.SaveAsync(journal, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// A cancellation during the isolated-failures loop used to leave
    /// <c>journal.State</c> stuck at <see cref="TransactionState.Applying"/>
    /// forever (only <see cref="DetermineIsolatedFinalState"/> — reached at
    /// the bottom of the normal loop — ever advanced it past that point).
    /// A journal persisted in that state is not one of the terminal states
    /// <see cref="ValidateExistingJournal"/> rejects, so a later run with the
    /// same transaction ID would try to resume it and hit an
    /// <see cref="InvalidOperationException"/> because the cancelled action's
    /// entry state no longer matches what resume expects — permanently
    /// wedging the transaction until the journal file was deleted by hand.
    /// This mirrors the same "mark remaining as skipped, compute the final
    /// state, persist" sequence used when a critical failure aborts the run.
    /// </summary>
    private async Task FinalizeCancelledIsolatedRunAsync(WindowsTransactionJournal journal)
    {
        foreach (var entry in journal.Actions.Where(entry =>
                     entry.State is ActionJournalState.Pending
                         or ActionJournalState.DeferredPrivilege))
        {
            MarkTerminal(entry, ActionJournalState.Skipped,
                ActionExecutionOutcome.NotRun, "Ignorada porque a operação foi cancelada.");
        }

        journal.State = DetermineIsolatedFinalState(journal);
        journal.Error = "A operação foi cancelada pelo usuário.";
        await journalStore.SaveAsync(journal, CancellationToken.None).ConfigureAwait(false);
    }

    private static string? FindUnmetPrerequisite(
        ActionMetadataDto metadata,
        IReadOnlyDictionary<string, WindowsActionJournalEntry> entriesById)
    {
        foreach (var prerequisiteId in metadata.Prerequisites)
        {
            if (!entriesById.TryGetValue(prerequisiteId, out var entry)
                || entry.Outcome is not (ActionExecutionOutcome.Verified or ActionExecutionOutcome.Applied))
            {
                return prerequisiteId;
            }
        }

        return null;
    }

    private static void MarkTerminal(
        WindowsActionJournalEntry entry,
        ActionJournalState state,
        ActionExecutionOutcome outcome,
        string reason)
    {
        entry.State = state;
        entry.Outcome = outcome;
        entry.OutcomeReason = reason;
        entry.CompletedAtUtc = DateTimeOffset.UtcNow;
    }

    private static TransactionState DetermineIsolatedFinalState(
        WindowsTransactionJournal journal)
    {
        if (journal.Actions.Any(entry => entry.Outcome is
            ActionExecutionOutcome.Failed
            or ActionExecutionOutcome.RolledBack
            or ActionExecutionOutcome.RollbackFailed))
        {
            return TransactionState.CommittedWithErrors;
        }

        return journal.Actions.Any(entry => entry.State is
            ActionJournalState.Pending or ActionJournalState.DeferredPrivilege)
            ? TransactionState.AwaitingElevation
            : TransactionState.Committed;
    }

    private static void ReportStep(
        WindowsActionContext context,
        (IWindowsOptimizationAction Action, WindowsActionJournalEntry Entry) item,
        int step,
        int totalSteps,
        int completedWeight,
        int totalWeight,
        ActionExecutionOutcome outcome)
    {
        context.Progress?.Report(new WindowsActionProgress(
            context.TransactionId,
            item.Action.Metadata.Id,
            item.Action.Metadata.Name,
            completedWeight,
            totalWeight,
            step,
            totalSteps,
            outcome));
    }

    private async Task RollbackAppliedAsync(
        WindowsTransactionJournal journal,
        IReadOnlyList<(IWindowsOptimizationAction Action, WindowsActionJournalEntry Entry)> applied,
        WindowsActionContext context,
        TransactionState successState,
        CancellationToken cancellationToken)
    {
        journal.State = TransactionState.RollingBack;
        await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
        var rollbackErrors = new List<Exception>();

        foreach (var item in applied.Reverse())
        {
            try
            {
                item.Entry.State = ActionJournalState.RollingBack;
                await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
                await item.Action.RollbackAsync(
                    context,
                    item.Entry.SnapshotJson,
                    cancellationToken).ConfigureAwait(false);
                item.Entry.State = ActionJournalState.RolledBack;
                item.Entry.Outcome = ActionExecutionOutcome.RolledBack;
                item.Entry.CompletedAtUtc = DateTimeOffset.UtcNow;
            }
            catch (Exception exception) when (exception is not StackOverflowException)
            {
                item.Entry.State = ActionJournalState.RollbackFailed;
                item.Entry.Outcome = ActionExecutionOutcome.RollbackFailed;
                item.Entry.Error = exception.ToString();
                rollbackErrors.Add(exception);
            }

            await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
        }

        if (rollbackErrors.Count == 0)
        {
            journal.State = successState;
        }
        else
        {
            journal.State = TransactionState.RollbackFailed;
            journal.Error = new AggregateException(rollbackErrors).ToString();
        }

        await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
    }
}
