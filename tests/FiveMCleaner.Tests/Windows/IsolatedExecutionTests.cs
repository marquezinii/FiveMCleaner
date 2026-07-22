using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Windows.Actions;
using FiveMCleaner.Windows.Engine;
using Xunit;

namespace FiveMCleaner.Tests.Windows;

public sealed class IsolatedExecutionTests
{
    private static readonly WindowsTransactionOptions Isolated = new()
    {
        IncludeStandardUserActions = true,
        IncludeAdministratorActions = false,
        IsolateFailures = true
    };

    [Fact]
    public async Task NonCriticalFailure_IsIsolated_OtherActionsStillRun()
    {
        // Limpeza (não crítica) falha; o Modo de Jogo, independente, ainda roda.
        var failing = ConfigurableTestAction.Failing(OptimizationActionIds.CleanUserTemporaryFiles);
        var succeeding = ConfigurableTestAction.Changing(OptimizationActionIds.EnableGameMode);
        var (engine, journals, id) = Build(failing, succeeding);

        var result = await engine.ExecuteAsync([failing, succeeding], Context(id), Isolated);

        Assert.Equal(WindowsTransactionState.CommittedWithErrors, result.State);
        Assert.NotNull(result.Error);
        var journal = journals.Get(id);
        Assert.Equal(ActionExecutionOutcome.Failed, OutcomeOf(journal, failing));
        Assert.Equal(ActionExecutionOutcome.Applied, OutcomeOf(journal, succeeding));
        Assert.Equal(1, succeeding.CommitCount);
    }

    [Fact]
    public async Task CriticalFailure_AbortsRemainingIndependentActions()
    {
        var criticalVerify = ConfigurableTestAction.Failing(OptimizationActionIds.VerifyFiveMIsStopped);
        var laterAction = ConfigurableTestAction.Changing(OptimizationActionIds.EnableGameMode);
        var (engine, journals, id) = Build(criticalVerify, laterAction);

        var result = await engine.ExecuteAsync([criticalVerify, laterAction], Context(id), Isolated);

        Assert.Equal(WindowsTransactionState.CommittedWithErrors, result.State);
        var journal = journals.Get(id);
        Assert.Equal(ActionExecutionOutcome.Failed, OutcomeOf(journal, criticalVerify));
        Assert.Equal(ActionExecutionOutcome.NotRun, OutcomeOf(journal, laterAction));
        Assert.Equal(0, laterAction.ApplyCount);
    }

    [Fact]
    public async Task UnmetPrerequisite_SkipsDependentAction()
    {
        // A poda de diagnósticos exige a verificação do FiveM; sem ela no conjunto,
        // a ação dependente é ignorada (não falha).
        var dependent = ConfigurableTestAction.Changing(OptimizationActionIds.PruneLegacyCrashDumps);
        var (engine, journals, id) = Build(dependent);

        var result = await engine.ExecuteAsync([dependent], Context(id), Isolated);

        Assert.Equal(WindowsTransactionState.Committed, result.State);
        var journal = journals.Get(id);
        Assert.Equal(ActionExecutionOutcome.Skipped, OutcomeOf(journal, dependent));
        Assert.Equal(0, dependent.ApplyCount);
    }

    [Fact]
    public async Task CommitFailure_RollsBackOnlyThatAction()
    {
        var healthy = ConfigurableTestAction.Changing(OptimizationActionIds.EnableGameMode);
        var commitFails = ConfigurableTestAction.CommitFailing(OptimizationActionIds.DisableBackgroundCapture);
        var (engine, journals, id) = Build(healthy, commitFails);

        var result = await engine.ExecuteAsync([healthy, commitFails], Context(id), Isolated);

        Assert.Equal(WindowsTransactionState.CommittedWithErrors, result.State);
        var journal = journals.Get(id);
        Assert.Equal(ActionExecutionOutcome.Applied, OutcomeOf(journal, healthy));
        Assert.Equal(ActionExecutionOutcome.RolledBack, OutcomeOf(journal, commitFails));
        Assert.Equal(0, healthy.RollbackCount);
        Assert.Equal(1, commitFails.RollbackCount);
    }

    [Fact]
    public async Task VerifiedAction_IsRecordedWithoutChange()
    {
        var verified = ConfigurableTestAction.NoChange(OptimizationActionIds.EnableGameMode);
        var (engine, journals, id) = Build(verified);

        var result = await engine.ExecuteAsync([verified], Context(id), Isolated);

        Assert.Equal(WindowsTransactionState.Committed, result.State);
        Assert.Null(result.Error);
        Assert.Equal(ActionExecutionOutcome.Verified, OutcomeOf(journals.Get(id), verified));
        Assert.Equal(0, verified.CommitCount);
    }

    private static (WindowsTransactionEngine Engine, InMemoryJournalStore Journals, Guid Id) Build(
        params ConfigurableTestAction[] actions)
    {
        var journals = new InMemoryJournalStore();
        var engine = new WindowsTransactionEngine(new WindowsActionCatalog(actions), journals);
        return (engine, journals, Guid.NewGuid());
    }

    private static ActionExecutionOutcome OutcomeOf(
        WindowsTransactionJournal journal,
        ConfigurableTestAction action)
    {
        return journal.Actions.Single(entry => entry.ActionId == action.Metadata.Id).Outcome;
    }

    private static WindowsActionContext Context(Guid transactionId)
    {
        return new WindowsActionContext
        {
            TransactionId = transactionId,
            StartedAtUtc = DateTimeOffset.UtcNow,
            IsElevated = false
        };
    }

    private sealed class ConfigurableTestAction : WindowsOptimizationAction
    {
        private readonly Behavior behavior;

        private ConfigurableTestAction(string actionId, Behavior behavior)
        {
            Metadata = WindowsActionMetadata.For(actionId);
            this.behavior = behavior;
        }

        private enum Behavior
        {
            Changing,
            NoChange,
            FailApply,
            FailCommit
        }

        public override ActionMetadataDto Metadata { get; }

        public int ApplyCount { get; private set; }

        public int CommitCount { get; private set; }

        public int RollbackCount { get; private set; }

        public static ConfigurableTestAction Changing(string id) => new(id, Behavior.Changing);

        public static ConfigurableTestAction NoChange(string id) => new(id, Behavior.NoChange);

        public static ConfigurableTestAction Failing(string id) => new(id, Behavior.FailApply);

        public static ConfigurableTestAction CommitFailing(string id) => new(id, Behavior.FailCommit);

        public override Task<WindowsActionApplyResult> ApplyAsync(
            WindowsActionContext context,
            CancellationToken cancellationToken)
        {
            ApplyCount++;
            if (behavior == Behavior.FailApply)
            {
                throw new InvalidOperationException("simulated apply failure");
            }

            if (behavior == Behavior.NoChange)
            {
                return Task.FromResult(WindowsActionApplyResult.NoChange("já estava correto"));
            }

            return Task.FromResult(WindowsActionApplyResult.ChangedWith(
                new Dictionary<string, string> { ["previous"] = "value" }));
        }

        public override Task CommitAsync(
            WindowsActionContext context,
            string? snapshotJson,
            CancellationToken cancellationToken)
        {
            CommitCount++;
            if (behavior == Behavior.FailCommit)
            {
                throw new InvalidOperationException("simulated commit failure");
            }

            return Task.CompletedTask;
        }

        public override Task RollbackAsync(
            WindowsActionContext context,
            string? snapshotJson,
            CancellationToken cancellationToken)
        {
            RollbackCount++;
            return Task.CompletedTask;
        }
    }
}
