using FiveMCleaner.Contracts;
using FiveMCleaner.Windows.Actions;

namespace FiveMCleaner.Windows.Engine;

public sealed record WindowsTransactionOptions
{
    public bool IncludeStandardUserActions { get; init; } = true;

    public bool IncludeAdministratorActions { get; init; } = true;

    public bool RollbackOnFailure { get; init; } = true;
}

public sealed record WindowsRollbackOptions
{
    public bool IncludeStandardUserActions { get; init; } = true;

    public bool IncludeAdministratorActions { get; init; } = true;
}

public sealed record WindowsTransactionResult
{
    public required Guid TransactionId { get; init; }

    public required WindowsTransactionState State { get; init; }

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
        ArgumentNullException.ThrowIfNull(requestedActions);
        ArgumentNullException.ThrowIfNull(context);
        if (context.TransactionId == Guid.Empty)
        {
            throw new ArgumentException("TransactionId cannot be empty.", nameof(context));
        }

        options ??= new WindowsTransactionOptions();
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

        await executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
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

            var entriesById = journal.Actions.ToDictionary(
                entry => entry.ActionId,
                StringComparer.Ordinal);
            var selected = new List<(IWindowsOptimizationAction Action, WindowsActionJournalEntry Entry)>();
            foreach (var action in actions)
            {
                var entry = entriesById[action.Metadata.Id];
                if (entry.State == WindowsActionJournalState.SkippedPrivilege)
                {
                    entry.State = WindowsActionJournalState.DeferredPrivilege;
                }

                if (entry.State == WindowsActionJournalState.Committed)
                {
                    continue;
                }

                if (entry.State is not (WindowsActionJournalState.Pending
                    or WindowsActionJournalState.DeferredPrivilege))
                {
                    throw new InvalidOperationException(
                        $"Action '{entry.ActionId}' cannot resume from state '{entry.State}'.");
                }

                var isAdministratorAction =
                    action.Metadata.RequiredPrivilege == RequiredPrivilege.Administrator;
                var include = isAdministratorAction
                    ? options.IncludeAdministratorActions && context.IsElevated
                    : options.IncludeStandardUserActions;
                if (!include)
                {
                    if (isAdministratorAction)
                    {
                        entry.State = WindowsActionJournalState.DeferredPrivilege;
                    }

                    continue;
                }

                selected.Add((action, entry));
            }

            if (selected.Count == 0)
            {
                journal.State = DetermineSuccessfulState(journal);
                await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
                return CreateResult(journal, [], GetDeferredAdministratorIds(journal), null);
            }

            journal.State = WindowsTransactionState.Applying;
            await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
            var applied = new List<(IWindowsOptimizationAction Action, WindowsActionJournalEntry Entry)>();
            try
            {
                var totalWeight = selected.Sum(item => Math.Max(1, item.Action.Metadata.ProgressWeight));
                var completedWeight = 0;

                foreach (var item in selected)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    item.Entry.State = WindowsActionJournalState.Applying;
                    item.Entry.StartedAtUtc = DateTimeOffset.UtcNow;
                    item.Entry.Error = null;
                    await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);

                    context.Progress?.Report(new WindowsActionProgress(
                        context.TransactionId,
                        item.Action.Metadata.Id,
                        $"Aplicando {item.Action.Metadata.Name}",
                        completedWeight,
                        totalWeight));

                    var result = await item.Action.ApplyAsync(context, cancellationToken)
                        .ConfigureAwait(false);
                    item.Entry.Changed = result.Changed;
                    item.Entry.SnapshotJson = result.SnapshotJson;
                    item.Entry.Messages.AddRange(result.Messages);
                    item.Entry.State = WindowsActionJournalState.Applied;
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

                journal.State = WindowsTransactionState.Committing;
                await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);

                foreach (var item in OrderForCommit(applied))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    item.Entry.State = WindowsActionJournalState.Committing;
                    await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
                    await item.Action.CommitAsync(
                        context,
                        item.Entry.SnapshotJson,
                        cancellationToken).ConfigureAwait(false);
                    item.Entry.State = WindowsActionJournalState.Committed;
                    item.Entry.CompletedAtUtc = DateTimeOffset.UtcNow;
                    await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
                }

                journal.State = DetermineSuccessfulState(journal);
                journal.Error = null;
                await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
                return CreateResult(
                    journal,
                    applied.Select(item => item.Action.Metadata.Id).ToArray(),
                    GetDeferredAdministratorIds(journal),
                    null);
            }
            catch (Exception exception) when (exception is not StackOverflowException)
            {
                journal.Error = exception.ToString();
                journal.State = WindowsTransactionState.Failed;
                var current = journal.Actions.LastOrDefault(entry =>
                    entry.State is WindowsActionJournalState.Applying
                        or WindowsActionJournalState.Committing);
                if (current is not null)
                {
                    current.State = WindowsActionJournalState.Failed;
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
                        WindowsTransactionState.RolledBack,
                        CancellationToken.None).ConfigureAwait(false);
                }

                return CreateResult(
                    journal,
                    applied.Select(item => item.Action.Metadata.Id).ToArray(),
                    GetDeferredAdministratorIds(journal),
                    exception.Message);
            }
        }
        finally
        {
            executionGate.Release();
        }
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
                ? WindowsTransactionState.AwaitingElevationRollback
                : hasRemainingStandardActions
                    ? WindowsTransactionState.AwaitingStandardRollback
                    : WindowsTransactionState.RolledBack;
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
                journal.State == WindowsTransactionState.RollbackFailed ? journal.Error : null);
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
            || entry.State is WindowsActionJournalState.RolledBack
                or WindowsActionJournalState.Pending
                or WindowsActionJournalState.DeferredPrivilege
                or WindowsActionJournalState.SkippedPrivilege)
        {
            return false;
        }

        if (entry.State == WindowsActionJournalState.Committed
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
            State = WindowsTransactionState.Created,
            Actions = actions.Select((action, index) => new WindowsActionJournalEntry
            {
                Sequence = index + 1,
                ActionId = action.Metadata.Id,
                Version = action.Metadata.Version,
                RequiredPrivilege = action.Metadata.RequiredPrivilege,
                Reversibility = action.Metadata.Reversibility,
                State = WindowsActionJournalState.Pending
            }).ToList()
        };
    }

    private static void ValidateExistingJournal(
        WindowsTransactionJournal journal,
        IReadOnlyList<IWindowsOptimizationAction> requestedActions)
    {
        if (journal.State is WindowsTransactionState.Failed
            or WindowsTransactionState.RollbackFailed
            or WindowsTransactionState.RollingBack
            or WindowsTransactionState.RolledBack
            or WindowsTransactionState.AwaitingElevationRollback
            or WindowsTransactionState.AwaitingStandardRollback)
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

    private static WindowsTransactionState DetermineSuccessfulState(
        WindowsTransactionJournal journal)
    {
        return journal.Actions.Any(entry => entry.State is
            WindowsActionJournalState.Pending or WindowsActionJournalState.DeferredPrivilege)
            ? WindowsTransactionState.AwaitingElevation
            : WindowsTransactionState.Committed;
    }

    private static IReadOnlyList<string> GetDeferredAdministratorIds(
        WindowsTransactionJournal journal)
    {
        return journal.Actions
            .Where(entry => entry.RequiredPrivilege == RequiredPrivilege.Administrator)
            .Where(entry => entry.State is
                WindowsActionJournalState.Pending or WindowsActionJournalState.DeferredPrivilege)
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

    private async Task RollbackAppliedAsync(
        WindowsTransactionJournal journal,
        IReadOnlyList<(IWindowsOptimizationAction Action, WindowsActionJournalEntry Entry)> applied,
        WindowsActionContext context,
        WindowsTransactionState successState,
        CancellationToken cancellationToken)
    {
        journal.State = WindowsTransactionState.RollingBack;
        await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
        var rollbackErrors = new List<Exception>();

        foreach (var item in applied.Reverse())
        {
            try
            {
                item.Entry.State = WindowsActionJournalState.RollingBack;
                await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
                await item.Action.RollbackAsync(
                    context,
                    item.Entry.SnapshotJson,
                    cancellationToken).ConfigureAwait(false);
                item.Entry.State = WindowsActionJournalState.RolledBack;
                item.Entry.CompletedAtUtc = DateTimeOffset.UtcNow;
            }
            catch (Exception exception) when (exception is not StackOverflowException)
            {
                item.Entry.State = WindowsActionJournalState.RollbackFailed;
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
            journal.State = WindowsTransactionState.RollbackFailed;
            journal.Error = new AggregateException(rollbackErrors).ToString();
        }

        await journalStore.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
    }
}
