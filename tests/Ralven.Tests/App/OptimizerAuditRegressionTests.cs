using Ralven.App.Services;
using Ralven.App.ViewModels;
using Ralven.Contracts;
using Ralven.Core.Catalog;
using Ralven.Tests.Windows;
using Ralven.Windows.Actions;
using Ralven.Windows.Engine;
using Xunit;

namespace Ralven.Tests.App;

public sealed class OptimizerAuditRegressionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Rollback_RestoresPowerSchemeBeforeItsOriginalAspmPolicy(bool cancelElevation, bool newerUserChoice)
    {
        using var directory = new TemporaryDirectory();
        var power = new FakePowerPlanController();
        var originalScheme = power.ActiveScheme;
        var originalPolicy = power.AspmPolicyState;
        var performance = new SessionPerformancePowerPlanAction(power, new FakePowerStatusProvider());
        var aspm = new PciExpressPowerManagementAction(power);
        var journals = new JsonWindowsTransactionJournalStore(directory.Combine("Transactions"));
        var engine = new WindowsTransactionEngine(new WindowsActionCatalog([performance, aspm]), journals);
        var context = Context();
        await engine.ExecuteAsync([performance, aspm], context, LocalOptions(), Token);
        await engine.ExecuteAsync([performance], context with { IsElevated = true }, AdminOptions(), Token);
        Assert.Equal(power.PerformanceScheme, power.ActiveScheme);

        var activeBeforeRollback = newerUserChoice ? Guid.NewGuid() : power.ActiveScheme;
        power.ActiveScheme = activeBeforeRollback;
        var adminCalls = 0;
        var restored = await new AppOptimizationService(directory.Path).RollbackCoreAsync(
            context.TransactionId, new Inline<AppProgressUpdate>(_ => { }), engine,
            async token =>
            {
                adminCalls++;
                if (cancelElevation) return false;
                var result = await engine.RollbackAsync(context.TransactionId, true,
                    new WindowsRollbackOptions { IncludeStandardUserActions = false }, token);
                return result.State is TransactionState.RolledBack or TransactionState.AwaitingStandardRollback;
            }, Token);

        Assert.Equal(1, adminCalls);
        if (cancelElevation || newerUserChoice)
        {
            Assert.False(restored);
            Assert.Equal(activeBeforeRollback, power.ActiveScheme);
            Assert.Equal(new PciExpressAspmPolicy(0, 0), power.AspmPolicyState);
            Assert.Equal(ActionJournalState.Committed,
                (await journals.LoadAsync(context.TransactionId, Token))!.Actions[1].State);
            return;
        }
        Assert.True(restored);
        Assert.Equal(originalScheme, power.ActiveScheme);
        Assert.Equal(originalPolicy, power.AspmPolicyState);
        Assert.Equal(TransactionState.RolledBack, (await journals.LoadAsync(context.TransactionId, Token))!.State);
    }

    [Fact]
    public async Task LocalFailure_DoesNotPreventIndependentAdministratorActionOrReplayFailedAction()
    {
        var failure = new FailingGameModeAction();
        var power = new FakePowerPlanController();
        var performance = new SessionPerformancePowerPlanAction(power, new FakePowerStatusProvider());
        var journals = new InMemoryJournalStore();
        var engine = new WindowsTransactionEngine(new WindowsActionCatalog([failure, performance]), journals);
        var context = Context();
        var local = await engine.ExecuteAsync([failure, performance], context, LocalOptions(), Token);
        Assert.Equal(TransactionState.CommittedWithErrors, local.State);
        Assert.Single(local.DeferredAdministratorActionIds);

        var admin = await engine.ExecuteAsync([performance], context with { IsElevated = true }, AdminOptions(), Token);
        Assert.Equal(power.PerformanceScheme, power.ActiveScheme);
        Assert.Empty(admin.DeferredAdministratorActionIds);
        Assert.Null(admin.Error);
        Assert.Equal(TransactionState.CommittedWithErrors, admin.State);
        Assert.False(OptimizationReportBuilder.Build(journals.Get(context.TransactionId), OptimizationProfile.Balanced).Succeeded);
        await engine.ExecuteAsync([failure, performance], context, LocalOptions(), Token);
        Assert.Equal(1, failure.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBetweenActions_FinalizesHistoryAndPreservesRollback(bool beforeNextApply)
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        var power = new FakePowerPlanController();
        var previousPolicy = power.AspmPolicyState;
        var aspm = new PciExpressPowerManagementAction(power);
        var diagnosis = new AuditDiagnostic();
        var journals = new JsonWindowsTransactionJournalStore(directory.Combine("Transactions"));
        var engine = new WindowsTransactionEngine(new WindowsActionCatalog([aspm, diagnosis]), journals);
        var context = Context() with
        {
            Progress = new Inline<WindowsActionProgress>(update =>
            {
                if (beforeNextApply
                    ? update.ActionId == diagnosis.Metadata.Id && update.Outcome == ActionExecutionOutcome.Pending
                    : update.ActionId == aspm.Metadata.Id && update.Outcome == ActionExecutionOutcome.Applied)
                    cancellation.Cancel();
            })
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.ExecuteAsync(
            [aspm, diagnosis], context, LocalOptions(), cancellation.Token));
        var journal = (await journals.LoadAsync(context.TransactionId, Token))!;
        Assert.Equal(TransactionState.CommittedWithErrors, journal.State);
        Assert.Equal(ActionExecutionOutcome.Applied, journal.Actions[0].Outcome);
        Assert.Equal(ActionExecutionOutcome.NotRun, journal.Actions[1].Outcome);
        var report = OptimizationReportBuilder.Build(journal, OptimizationProfile.Balanced);
        Assert.False(report.Succeeded);
        Assert.Equal(1, report.NotRunCount);
        var history = await new AppOptimizationService(directory.Path).LoadHistoryAsync(Token);
        Assert.True(Assert.Single(history).CanRollback);
        var restored = await engine.RollbackAsync(context.TransactionId, false, cancellationToken: Token);
        Assert.Equal(TransactionState.RolledBack, restored.State);
        Assert.Equal(previousPolicy, power.AspmPolicyState);
    }

    [Fact]
    public async Task DiagnosticDetail_SurvivesExecutionAndTechnicalReport()
    {
        var diagnosis = new AuditDiagnostic();
        var journals = new InMemoryJournalStore();
        var engine = new WindowsTransactionEngine(new WindowsActionCatalog([diagnosis]), journals);
        var context = Context();
        await engine.ExecuteAsync([diagnosis], context, LocalOptions(), Token);
        var report = OptimizationReportBuilder.Build(journals.Get(context.TransactionId), OptimizationProfile.Light);
        Assert.Equal(AuditDiagnostic.Message, Assert.Single(report.Lines).Reason);
        Assert.Contains(AuditDiagnostic.Message, TechnicalReportBuilder.Build(report, null, LocalizationService.Current));
    }

    [Fact]
    public async Task CompletedOptimization_CanReturnToPreparationWithoutLosingProfile()
    {
        var service = new FakeAppOptimizationService(new AppSettings(), false);
        using var vm = new MainViewModel(service);
        await vm.InitializeAsync();
        vm.SelectProfile(OptimizationProfile.Light);
        var report = OptimizationReportBuilder.Build(new WindowsTransactionJournal
        {
            TransactionId = Guid.NewGuid(),
            SchemaVersion = 1,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            State = TransactionState.Committed,
            WasElevated = false
        }, OptimizationProfile.Light);
        service.ExecutionResult = new AppOptimizationResult
        {
            TransactionId = report.TransactionId,
            Succeeded = true,
            WasCancelled = false,
            Summary = "Complete",
            CompletedActions = 0,
            BytesFreed = 0,
            Report = report
        };
        await vm.StartOptimizationAsync();
        Assert.True(vm.IsReportAvailable);
        vm.PrepareNewOptimization();
        Assert.True(vm.IsOptimizerIdle);
        Assert.True(vm.CanStart);
        Assert.True(vm.IsLightSelected);
        Assert.False(vm.IsReportAvailable);
        Assert.False(vm.CanShareReport);
        Assert.Empty(vm.StepLedger);
    }

    [Fact]
    public async Task CancelledResult_IsDisplayedAndHistoryIsRefreshed()
    {
        var service = new FakeAppOptimizationService(new AppSettings(), false);
        using var vm = new MainViewModel(service);
        await vm.InitializeAsync();
        var initialLoads = service.HistoryLoadCount;
        var report = OptimizationReportBuilder.Build(new WindowsTransactionJournal
        {
            TransactionId = Guid.NewGuid(),
            SchemaVersion = 1,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            State = TransactionState.CommittedWithErrors,
            WasElevated = false
        }, OptimizationProfile.Light);
        service.ExecutionResult = new AppOptimizationResult
        {
            TransactionId = report.TransactionId,
            Succeeded = false,
            WasCancelled = true,
            Summary = "Cancelled safely",
            CompletedActions = 0,
            BytesFreed = 0,
            Report = report
        };
        await vm.StartOptimizationAsync();
        Assert.True(vm.IsReportAvailable);
        Assert.False(vm.ReportSucceeded);
        Assert.Equal(initialLoads + 1, service.HistoryLoadCount);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task LocalOnlyRollback_DoesNotRequestAdministratorAccess()
    {
        using var directory = new TemporaryDirectory();
        var power = new FakePowerPlanController();
        var previousPolicy = power.AspmPolicyState;
        var aspm = new PciExpressPowerManagementAction(power);
        var journals = new JsonWindowsTransactionJournalStore(directory.Combine("Transactions"));
        var engine = new WindowsTransactionEngine(new WindowsActionCatalog([aspm]), journals);
        var context = Context();
        await engine.ExecuteAsync([aspm], context, LocalOptions(), Token);
        var restored = await new AppOptimizationService(directory.Path).RollbackCoreAsync(
            context.TransactionId, new Inline<AppProgressUpdate>(_ => { }), engine,
            _ => throw new InvalidOperationException("Local-only rollback must not elevate."), Token);
        Assert.True(restored);
        Assert.Equal(previousPolicy, power.AspmPolicyState);
    }

    private static WindowsActionContext Context() => new()
    {
        TransactionId = Guid.NewGuid(),
        StartedAtUtc = DateTimeOffset.UtcNow,
        IsElevated = false,
        Profile = OptimizationProfile.Balanced
    };

    private static WindowsTransactionOptions LocalOptions() => new()
    { IncludeAdministratorActions = false, IsolateFailures = true };

    private static WindowsTransactionOptions AdminOptions() => new()
    { IncludeStandardUserActions = false, IncludeAdministratorActions = true };

    private sealed class Inline<T>(Action<T> report) : IProgress<T>
    { public void Report(T value) => report(value); }

    private sealed class AuditDiagnostic : ReadOnlyDiagnosticAction
    {
        internal const string Message = "Antivirus unavailable; firewall needs attention.";
        public override ActionMetadataDto Metadata => WindowsActionMetadata.For(OptimizationActionIds.DiagnoseWindowsSecurityHealth);
        protected override string Describe() => Message;
    }

    private sealed class FailingGameModeAction : WindowsOptimizationAction
    {
        public int Calls { get; private set; }
        public override ActionMetadataDto Metadata => WindowsActionMetadata.For(OptimizationActionIds.EnableGameMode);
        public override Task<WindowsActionApplyResult> ApplyAsync(WindowsActionContext context, CancellationToken cancellationToken)
        { Calls++; throw new IOException("Simulated local failure."); }
        public override Task RollbackAsync(WindowsActionContext context, string? snapshotJson, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
