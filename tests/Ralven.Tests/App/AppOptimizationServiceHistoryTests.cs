using System.Globalization;
using System.Text.Json;
using Ralven.App.Services;
using Ralven.App.ViewModels;
using Ralven.Contracts;
using Ralven.Core.Catalog;
using Ralven.Windows.Engine;
using Xunit;

namespace Ralven.Tests.App;

public sealed class AppOptimizationServiceHistoryTests
{
    [Fact]
    public async Task LoadHistory_UsesPersistedProfileInsteadOfActionNameHeuristics()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var transactionId = Guid.NewGuid();
        var definition = ActionCatalog.Current.GetRequired(
            OptimizationActionIds.ReduceWindowsVisualEffects);
        var journal = new WindowsTransactionJournal
        {
            TransactionId = transactionId,
            SchemaVersion = 1,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            WasElevated = false,
            Profile = OptimizationProfile.Aggressive,
            State = TransactionState.Committed,
            Actions =
            [
                new WindowsActionJournalEntry
                {
                    Sequence = 1,
                    ActionId = definition.Id,
                    Version = definition.Version,
                    RequiredPrivilege = definition.RequiredPrivilege,
                    Reversibility = definition.Reversibility,
                    State = ActionJournalState.Committed,
                    Outcome = ActionExecutionOutcome.Applied,
                    Changed = true,
                    SnapshotJson = "{}"
                }
            ]
        };
        await WriteJournalAsync(temporaryDirectory.Path, journal);

        var history = await new AppOptimizationService(temporaryDirectory.Path)
            .LoadHistoryAsync(TestContext.Current.CancellationToken);

        Assert.Equal(OptimizationProfile.Aggressive, Assert.Single(history).Profile);
    }

    [Fact]
    public async Task LoadReport_RebuildsPersistedFailureAfterServiceRestart()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var definition = ActionCatalog.Current.GetRequired(
            OptimizationActionIds.ReduceWindowsVisualEffects);
        var journal = Journal(definition, TransactionState.CommittedWithErrors);
        journal.Actions[0] = journal.Actions[0] with
        {
            State = ActionJournalState.Failed,
            Outcome = ActionExecutionOutcome.Failed,
            Changed = false,
            SnapshotJson = null,
            OutcomeReason = "Windows did not apply the requested visual effects settings."
        };
        await WriteJournalAsync(temporaryDirectory.Path, journal);

        var report = await new AppOptimizationService(temporaryDirectory.Path)
            .LoadReportAsync(journal.TransactionId, TestContext.Current.CancellationToken);

        Assert.NotNull(report);
        Assert.Equal(journal.TransactionId, report.TransactionId);
        Assert.Equal(OptimizationProfile.Balanced, report.Profile);
        Assert.Equal(ActionExecutionOutcome.Failed, Assert.Single(report.Lines).Outcome);
        Assert.Equal(journal.Actions[0].OutcomeReason, Assert.Single(report.Lines).Reason);
    }

    [Fact]
    public async Task OpenHistoryReport_LoadsTheSelectedRunIntoTheResultView()
    {
        var transactionId = Guid.NewGuid();
        var report = new OptimizationReportDto
        {
            TransactionId = transactionId,
            Profile = OptimizationProfile.Aggressive,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            VerifiedCount = 1,
            ChangedCount = 0,
            SkippedCount = 0,
            WarningCount = 0,
            FailedCount = 0,
            RollbackFailedCount = 0,
            NotRunCount = 0,
            RequiresRestart = false,
            RestorePossible = false,
            Succeeded = true,
            Lines =
            [
                new OptimizationReportLineDto
                {
                    Sequence = 1,
                    ActionId = OptimizationActionIds.DiagnoseBottleneck,
                    ActionName = "Bottleneck",
                    Category = ActionCategory.Safety,
                    Outcome = ActionExecutionOutcome.Verified
                }
            ]
        };
        using var viewModel = new MainViewModel(new FakeAppOptimizationService(
            new AppSettings(),
            settingsFileExists: false,
            report: report));
        await viewModel.InitializeAsync();

        var opened = await viewModel.OpenHistoryReportAsync(new HistoryDisplayItem(
            transactionId,
            "Aggressive",
            "today",
            "success",
            CanRollback: false));

        Assert.True(opened);
        Assert.True(viewModel.IsReportAvailable);
        Assert.Single(viewModel.ReportLines);
        Assert.Equal($"Ralven-Report-{transactionId:N}.txt", viewModel.SuggestedReportFileName);
    }

    [Fact]
    public async Task LoadHistory_LegacyJournalInfersAggressiveVisualEffectsProfile()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var definition = ActionCatalog.Current.GetRequired(
            OptimizationActionIds.ReduceWindowsVisualEffects);
        var journal = Journal(definition, TransactionState.Committed) with
        {
            Profile = null
        };
        await WriteJournalAsync(temporaryDirectory.Path, journal);

        var history = await new AppOptimizationService(temporaryDirectory.Path)
            .LoadHistoryAsync(TestContext.Current.CancellationToken);

        Assert.Equal(OptimizationProfile.Aggressive, Assert.Single(history).Profile);
    }

    [Theory]
    [InlineData(OptimizationActionIds.ApplyGtaVGraphicsLaunchParameters)]
    [InlineData(OptimizationActionIds.RepairStaleAuthData)]
    public async Task LoadHistory_LegacySnapshotVersionDoesNotOfferUnsafeRollback(string actionId)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var definition = ActionCatalog.Current.GetRequired(actionId);
        Assert.Equal(2, definition.Version);
        var journal = Journal(definition, TransactionState.CommittedWithErrors);
        journal.Actions[0] = journal.Actions[0] with
        {
            Version = 1,
            State = ActionJournalState.Failed,
            Outcome = ActionExecutionOutcome.Failed,
            RollbackSafeAfterInterruption = true
        };
        await WriteJournalAsync(temporaryDirectory.Path, journal);

        var history = await new AppOptimizationService(temporaryDirectory.Path)
            .LoadHistoryAsync(TestContext.Current.CancellationToken);

        Assert.False(Assert.Single(history).CanRollback);
    }

    [Fact]
    public async Task LoadHistory_InterruptedRebuildableApplyOffersSafeRollback()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var definition = ActionCatalog.Current.GetRequired(
            OptimizationActionIds.RepairLegacyServerCache);
        var journal = Journal(definition, TransactionState.CommittedWithErrors);
        journal.Actions[0] = journal.Actions[0] with
        {
            State = ActionJournalState.Failed,
            Outcome = ActionExecutionOutcome.Failed,
            RollbackSafeAfterInterruption = true
        };
        await WriteJournalAsync(temporaryDirectory.Path, journal);

        var history = await new AppOptimizationService(temporaryDirectory.Path)
            .LoadHistoryAsync(TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(history).CanRollback);
    }

    [Fact]
    public async Task LoadHistory_LabelsCommittedWithErrorsHonestly()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var definition = ActionCatalog.Current.GetRequired(OptimizationActionIds.EnableGameMode);
        await WriteJournalAsync(
            temporaryDirectory.Path,
            Journal(definition, TransactionState.CommittedWithErrors));
        var localization = new LocalizationService(CultureInfo.GetCultureInfo("pt-BR"));

        var history = await new AppOptimizationService(temporaryDirectory.Path, localization)
            .LoadHistoryAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            localization.GetString("History.State.CommittedWithErrors"),
            Assert.Single(history).State);
    }

    [Fact]
    public async Task LoadHistory_LegacyAdministratorJournalWithoutReceiptDisablesUnsafeRollback()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var definition = ActionCatalog.Current.GetRequired(
            OptimizationActionIds.EnableSessionPerformancePowerPlan);
        await WriteJournalAsync(
            temporaryDirectory.Path,
            Journal(definition, TransactionState.Committed));
        var localization = new LocalizationService(CultureInfo.GetCultureInfo("pt-BR"));

        var history = await new AppOptimizationService(
                temporaryDirectory.Path,
                localization,
                _ => false)
            .LoadHistoryAsync(TestContext.Current.CancellationToken);

        var record = Assert.Single(history);
        Assert.False(record.CanRollback);
        Assert.Equal(localization.GetString("History.State.AdminReceiptMissing"), record.State);
    }

    [Fact]
    public async Task LoadHistory_AuthenticatedAdministratorJournalOffersRollback()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var definition = ActionCatalog.Current.GetRequired(
            OptimizationActionIds.EnableSessionPerformancePowerPlan);
        await WriteJournalAsync(
            temporaryDirectory.Path,
            Journal(definition, TransactionState.Committed));

        var history = await new AppOptimizationService(
                temporaryDirectory.Path,
                administratorReceiptExists: _ => true)
            .LoadHistoryAsync(TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(history).CanRollback);
    }

    [Fact]
    public async Task LoadHistory_AdministratorReceiptIsReadOutsideTheCallingThread()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var definition = ActionCatalog.Current.GetRequired(
            OptimizationActionIds.EnableSessionPerformancePowerPlan);
        await WriteJournalAsync(
            temporaryDirectory.Path,
            Journal(definition, TransactionState.Committed));
        var receiptThread = 0;
        var callerThread = 0;
        var cancellationToken = TestContext.Current.CancellationToken;
        var started = new TaskCompletionSource<Task<IReadOnlyList<AppHistoryRecord>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new AppOptimizationService(
            temporaryDirectory.Path,
            administratorReceiptExists: _ =>
            {
                receiptThread = Environment.CurrentManagedThreadId;
                return true;
            });
        var thread = new Thread(() =>
        {
            callerThread = Environment.CurrentManagedThreadId;
            started.SetResult(service.LoadHistoryAsync(cancellationToken));
        }) { IsBackground = true };

        thread.Start();
        var history = await started.Task.Unwrap().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(Assert.Single(history).CanRollback);
        Assert.NotEqual(0, receiptThread);
        Assert.NotEqual(callerThread, receiptThread);
    }

    [Fact]
    public async Task LoadHistory_CancelledBeforeScan_DoesNotReadReceipts()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var definition = ActionCatalog.Current.GetRequired(
            OptimizationActionIds.EnableSessionPerformancePowerPlan);
        await WriteJournalAsync(
            temporaryDirectory.Path,
            Journal(definition, TransactionState.Committed));
        var receiptRead = false;
        var service = new AppOptimizationService(
            temporaryDirectory.Path,
            administratorReceiptExists: _ => receiptRead = true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.LoadHistoryAsync(cancellation.Token));

        Assert.False(receiptRead);
    }

    [Fact]
    public async Task LoadHistory_LockedJournalDoesNotHideOtherRecords()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var definition = ActionCatalog.Current.GetRequired(OptimizationActionIds.EnableGameMode);
        await WriteJournalAsync(
            temporaryDirectory.Path,
            Journal(definition, TransactionState.Committed));
        var lockedPath = Path.Combine(
            temporaryDirectory.Path,
            "Transactions",
            $"{Guid.NewGuid():N}.json");
        using var lockedJournal = new FileStream(
            lockedPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None);

        var history = await new AppOptimizationService(temporaryDirectory.Path)
            .LoadHistoryAsync(TestContext.Current.CancellationToken);

        Assert.Single(history);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("pt-BR")]
    [InlineData("es")]
    public void AdministrativeFailureMessages_AreGenericForAnyBrokerAction(string cultureName)
    {
        var localization = new LocalizationService(CultureInfo.GetCultureInfo(cultureName));

        var cancelled = localization.GetString("Runtime.UacCancelledPreserved");
        var failed = localization.Format("Runtime.AdminPhaseFailedPreserved", "failure");

        Assert.DoesNotContain("power plan", cancelled, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("plano de energia", cancelled, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("plan de energía", cancelled, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("power plan", failed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("plano de energia", failed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("plan de energía", failed, StringComparison.OrdinalIgnoreCase);
    }

    private static WindowsTransactionJournal Journal(
        OptimizationActionDefinition definition,
        TransactionState state)
    {
        return new WindowsTransactionJournal
        {
            TransactionId = Guid.NewGuid(),
            SchemaVersion = 1,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            WasElevated = false,
            Profile = OptimizationProfile.Balanced,
            State = state,
            Actions =
            [
                new WindowsActionJournalEntry
                {
                    Sequence = 1,
                    ActionId = definition.Id,
                    Version = definition.Version,
                    RequiredPrivilege = definition.RequiredPrivilege,
                    Reversibility = definition.Reversibility,
                    State = ActionJournalState.Committed,
                    Outcome = ActionExecutionOutcome.Applied,
                    Changed = true,
                    SnapshotJson = "{}"
                }
            ]
        };
    }

    private static async Task WriteJournalAsync(
        string appDataDirectory,
        WindowsTransactionJournal journal)
    {
        var journalDirectory = Path.Combine(appDataDirectory, "Transactions");
        Directory.CreateDirectory(journalDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(journalDirectory, $"{journal.TransactionId:N}.json"),
            JsonSerializer.Serialize(journal, RalvenJson.Options),
            TestContext.Current.CancellationToken);
    }
}
