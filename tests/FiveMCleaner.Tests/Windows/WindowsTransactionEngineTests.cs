using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Windows.Actions;
using FiveMCleaner.Windows.Engine;
using Xunit;

namespace FiveMCleaner.Tests.Windows;

public sealed class WindowsTransactionEngineTests
{
    [Fact]
    public async Task TwoPhaseExecution_PreservesJournalAndCommitsAdministratorAction()
    {
        var standard = new TestGameModeAction();
        var administrator = new TestPowerAction();
        var journals = new InMemoryJournalStore();
        var engine = new WindowsTransactionEngine(
            new WindowsActionCatalog([standard, administrator]),
            journals);
        var transactionId = Guid.NewGuid();

        var first = await engine.ExecuteAsync(
            [standard, administrator],
            Context(transactionId, elevated: false));

        Assert.Equal(WindowsTransactionState.AwaitingElevation, first.State);
        Assert.Equal([standard.Metadata.Id], first.AppliedActionIds);
        Assert.Equal([administrator.Metadata.Id], first.DeferredAdministratorActionIds);
        Assert.Equal(1, standard.ApplyCount);
        Assert.Equal(0, administrator.ApplyCount);
        Assert.Equal(2, journals.Get(transactionId).Actions.Count);

        var second = await engine.ExecuteAsync(
            [administrator],
            Context(transactionId, elevated: true),
            new WindowsTransactionOptions
            {
                IncludeStandardUserActions = false,
                IncludeAdministratorActions = true
            });

        Assert.Equal(WindowsTransactionState.Committed, second.State);
        Assert.Equal([administrator.Metadata.Id], second.AppliedActionIds);
        Assert.Empty(second.DeferredAdministratorActionIds);
        Assert.Equal(1, administrator.ApplyCount);
        var journal = journals.Get(transactionId);
        Assert.True(journal.WasElevated);
        Assert.Equal(2, journal.Actions.Count);
        Assert.All(journal.Actions, entry =>
            Assert.Equal(WindowsActionJournalState.Committed, entry.State));

        var repeated = await engine.ExecuteAsync(
            [administrator],
            Context(transactionId, elevated: true));
        Assert.Equal(WindowsTransactionState.Committed, repeated.State);
        Assert.Empty(repeated.AppliedActionIds);
        Assert.Equal(1, administrator.ApplyCount);
    }

    [Fact]
    public async Task Rollback_CompletesInStandardAndElevatedPhases()
    {
        var standard = new TestGameModeAction();
        var administrator = new TestPowerAction();
        var journals = new InMemoryJournalStore();
        var engine = new WindowsTransactionEngine(
            new WindowsActionCatalog([standard, administrator]),
            journals);
        var transactionId = Guid.NewGuid();
        _ = await engine.ExecuteAsync(
            [standard, administrator],
            Context(transactionId, elevated: false));
        _ = await engine.ExecuteAsync(
            [administrator],
            Context(transactionId, elevated: true));

        var standardRollback = await engine.RollbackAsync(
            transactionId,
            isElevated: false);

        Assert.Equal(WindowsTransactionState.AwaitingElevationRollback, standardRollback.State);
        Assert.Equal([administrator.Metadata.Id], standardRollback.DeferredAdministratorActionIds);
        Assert.Equal(1, standard.RollbackCount);
        Assert.Equal(0, administrator.RollbackCount);

        var elevatedRollback = await engine.RollbackAsync(
            transactionId,
            isElevated: true);

        Assert.Equal(WindowsTransactionState.RolledBack, elevatedRollback.State);
        Assert.Empty(elevatedRollback.DeferredAdministratorActionIds);
        Assert.Equal(1, standard.RollbackCount);
        Assert.Equal(1, administrator.RollbackCount);
        Assert.All(journals.Get(transactionId).Actions, entry =>
            Assert.Equal(WindowsActionJournalState.RolledBack, entry.State));
    }

    [Fact]
    public async Task ElevatedAdminOnlyRollback_NeverExecutesStandardSnapshots()
    {
        var standard = new TestGameModeAction();
        var administrator = new TestPowerAction();
        var journals = new InMemoryJournalStore();
        var engine = new WindowsTransactionEngine(
            new WindowsActionCatalog([standard, administrator]),
            journals);
        var transactionId = Guid.NewGuid();
        _ = await engine.ExecuteAsync(
            [standard, administrator],
            Context(transactionId, elevated: false));
        _ = await engine.ExecuteAsync(
            [administrator],
            Context(transactionId, elevated: true));

        var result = await engine.RollbackAsync(
            transactionId,
            isElevated: true,
            new WindowsRollbackOptions
            {
                IncludeStandardUserActions = false,
                IncludeAdministratorActions = true
            });

        Assert.Equal(WindowsTransactionState.AwaitingStandardRollback, result.State);
        Assert.Equal(0, standard.RollbackCount);
        Assert.Equal(1, administrator.RollbackCount);
        Assert.Equal(
            WindowsActionJournalState.Committed,
            journals.Get(transactionId).Actions[0].State);
    }

    [Fact]
    public async Task Rollback_RejectsTamperedPrivilegeMetadataBeforeExecutingSnapshots()
    {
        var standard = new TestGameModeAction();
        var administrator = new TestPowerAction();
        var journals = new InMemoryJournalStore();
        var engine = new WindowsTransactionEngine(
            new WindowsActionCatalog([standard, administrator]),
            journals);
        var transactionId = Guid.NewGuid();
        _ = await engine.ExecuteAsync(
            [standard, administrator],
            Context(transactionId, elevated: false));

        var journal = journals.Get(transactionId);
        journal.Actions[0] = journal.Actions[0] with
        {
            RequiredPrivilege = RequiredPrivilege.Administrator
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => engine.RollbackAsync(
            transactionId,
            isElevated: true,
            new WindowsRollbackOptions
            {
                IncludeStandardUserActions = false,
                IncludeAdministratorActions = true
            }));
        Assert.Equal(0, standard.RollbackCount);
        Assert.Equal(0, administrator.RollbackCount);
    }

    [Fact]
    public async Task FailedApply_RollsBackAlreadyAppliedActionsInReverseTransaction()
    {
        var standard = new TestGameModeAction();
        var failing = new TestFailingCaptureAction();
        var journals = new InMemoryJournalStore();
        var engine = new WindowsTransactionEngine(
            new WindowsActionCatalog([standard, failing]),
            journals);
        var transactionId = Guid.NewGuid();

        var result = await engine.ExecuteAsync(
            [standard, failing],
            Context(transactionId, elevated: false));

        Assert.Equal(WindowsTransactionState.RolledBack, result.State);
        Assert.NotNull(result.Error);
        Assert.Equal(1, standard.RollbackCount);
        Assert.Equal(WindowsActionJournalState.RolledBack, journals.Get(transactionId).Actions[0].State);
        Assert.Equal(WindowsActionJournalState.Failed, journals.Get(transactionId).Actions[1].State);
    }

    private static WindowsActionContext Context(Guid transactionId, bool elevated)
    {
        return new WindowsActionContext
        {
            TransactionId = transactionId,
            StartedAtUtc = DateTimeOffset.UtcNow,
            IsElevated = elevated
        };
    }

    private abstract class TestAction : WindowsOptimizationAction
    {
        public int ApplyCount { get; private set; }

        public int CommitCount { get; private set; }

        public int RollbackCount { get; private set; }

        protected virtual bool Fails => false;

        public override Task<WindowsActionApplyResult> ApplyAsync(
            WindowsActionContext context,
            CancellationToken cancellationToken)
        {
            ApplyCount++;
            if (Fails)
            {
                throw new InvalidOperationException("simulated failure");
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

    private sealed class TestGameModeAction : TestAction
    {
        public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
            OptimizationActionIds.EnableGameMode);
    }

    private sealed class TestPowerAction : TestAction
    {
        public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
            OptimizationActionIds.EnableSessionPerformancePowerPlan);
    }

    private sealed class TestFailingCaptureAction : TestAction
    {
        protected override bool Fails => true;

        public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
            OptimizationActionIds.DisableBackgroundCapture);
    }
}
