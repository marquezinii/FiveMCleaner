using Ralven.Contracts;
using Ralven.Core.Catalog;
using Ralven.Windows.Engine;
using Xunit;

namespace Ralven.Tests.Windows;

public sealed class OptimizationReportBuilderTests
{
    [Fact]
    public void Build_PreservesPersonalRoutineForHistoryAndTechnicalReports()
    {
        var journal = Journal() with { PersonalUsage = PersonalUsage.Streaming };
        var report = OptimizationReportBuilder.Build(journal, OptimizationProfile.Aggressive);
        Assert.Equal(PersonalUsage.Streaming, report.PersonalUsage);
        var text = Ralven.App.Services.TechnicalReportBuilder.Build(report, null,
            new Ralven.App.Services.LocalizationService(System.Globalization.CultureInfo.GetCultureInfo("en-US")));
        Assert.Contains("Ultra", text);
        Assert.DoesNotContain("Aggressive", text);
    }

    [Fact]
    public void Build_CountsOutcomesAndNeverClaimsSuccessWhenAnActionFailed()
    {
        var journal = Journal(
            Entry(1, OptimizationActionIds.VerifyFiveMIsStopped, ActionExecutionOutcome.Verified),
            Entry(2, OptimizationActionIds.EnableGameMode, ActionExecutionOutcome.Applied),
            Entry(3, OptimizationActionIds.CleanUserTemporaryFiles, ActionExecutionOutcome.Applied),
            Entry(4, OptimizationActionIds.RepairLegacyServerCache, ActionExecutionOutcome.Skipped),
            Entry(5, OptimizationActionIds.DisableBackgroundCapture, ActionExecutionOutcome.Failed));

        var report = OptimizationReportBuilder.Build(journal, OptimizationProfile.Balanced);

        Assert.Equal(1, report.VerifiedCount);
        Assert.Equal(2, report.ChangedCount);
        Assert.Equal(1, report.SkippedCount);
        Assert.Equal(1, report.FailedCount);
        Assert.False(report.Succeeded);
        Assert.True(report.RestorePossible); // EnableGameMode é totalmente reversível
        Assert.Equal(5, report.Lines.Count);
    }

    [Fact]
    public void Build_ReportsSuccessWhenOnlyVerifiedOrApplied()
    {
        var journal = Journal(
            Entry(1, OptimizationActionIds.VerifyFiveMIsStopped, ActionExecutionOutcome.Verified),
            Entry(2, OptimizationActionIds.EnableGameMode, ActionExecutionOutcome.Applied));

        // A successful report also requires a confirmed terminal transaction.
        journal.State = TransactionState.Committed;
        var report = OptimizationReportBuilder.Build(journal, OptimizationProfile.Light);

        Assert.True(report.Succeeded);
        Assert.Equal(0, report.FailedCount);
        Assert.Equal(0, report.RollbackFailedCount);
    }

    [Fact]
    public void Build_RollbackFailureIsNotSuccessAndIsCountedSeparately()
    {
        var journal = Journal(
            Entry(1, OptimizationActionIds.EnableGameMode, ActionExecutionOutcome.RollbackFailed));

        var report = OptimizationReportBuilder.Build(journal, OptimizationProfile.Aggressive);

        Assert.False(report.Succeeded);
        Assert.Equal(1, report.RollbackFailedCount);
    }

    [Fact]
    public void Build_PropagatesBugCodeFromJournalEntryToReportLine()
    {
        var entry = Entry(1, OptimizationActionIds.DisableBackgroundCapture, ActionExecutionOutcome.Failed);
        entry.BugCode = BugCode.WIN_GAMING_MODE;
        var journal = Journal(entry);

        var report = OptimizationReportBuilder.Build(journal, OptimizationProfile.Balanced);

        Assert.Equal(BugCode.WIN_GAMING_MODE, report.Lines[0].BugCode);
    }

    [Fact]
    public void Build_LineWithoutBugCode_ReportsNullNotAFakeDefault()
    {
        var journal = Journal(Entry(1, OptimizationActionIds.EnableGameMode, ActionExecutionOutcome.Applied));

        var report = OptimizationReportBuilder.Build(journal, OptimizationProfile.Light);

        Assert.Null(report.Lines[0].BugCode);
    }

    [Theory]
    [InlineData(TransactionState.Created)]
    [InlineData(TransactionState.Applying)]
    [InlineData(TransactionState.AwaitingElevation)]
    [InlineData(TransactionState.CommittedWithErrors)]
    public void Build_UnconfirmedTransactionNeverReportsSuccess(TransactionState state)
    {
        var journal = Journal(Entry(1, OptimizationActionIds.EnableGameMode, ActionExecutionOutcome.Applied));
        journal.State = state;
        Assert.False(OptimizationReportBuilder.Build(journal, OptimizationProfile.Light).Succeeded);
    }

    [Theory]
    [InlineData(ActionExecutionOutcome.Pending)]
    [InlineData(ActionExecutionOutcome.NotRun)]
    public void Build_UnfinishedActionsNeverReportSuccess(ActionExecutionOutcome outcome)
    {
        var entry = Entry(1, OptimizationActionIds.EnableGameMode, outcome);
        entry.State = ActionJournalState.Pending;
        var journal = Journal(entry);
        journal.State = TransactionState.Committed;
        Assert.False(OptimizationReportBuilder.Build(journal, OptimizationProfile.Light).Succeeded);
    }

    [Fact]
    public void Build_FailureReasonTakesPrecedenceOverEarlierActionMessages()
    {
        var entry = Entry(1, OptimizationActionIds.EnableGameMode, ActionExecutionOutcome.Failed);
        entry.Messages.Add("The setting was changed before verification failed.");
        entry.OutcomeReason = "The setting could not be verified.";
        var report = OptimizationReportBuilder.Build(Journal(entry), OptimizationProfile.Light);
        Assert.Equal(entry.OutcomeReason, Assert.Single(report.Lines).Reason);
    }

    private static WindowsTransactionJournal Journal(params WindowsActionJournalEntry[] entries)
    {
        return new WindowsTransactionJournal
        {
            TransactionId = Guid.NewGuid(),
            SchemaVersion = 1,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            WasElevated = false,
            State = TransactionState.CommittedWithErrors,
            Actions = entries.ToList()
        };
    }

    private static WindowsActionJournalEntry Entry(
        int sequence,
        string actionId,
        ActionExecutionOutcome outcome)
    {
        var definition = ActionCatalog.Current.GetRequired(actionId);
        return new WindowsActionJournalEntry
        {
            Sequence = sequence,
            ActionId = actionId,
            Version = definition.Version,
            RequiredPrivilege = definition.RequiredPrivilege,
            Reversibility = definition.Reversibility,
            State = ActionJournalState.Committed,
            Outcome = outcome,
            Changed = outcome == ActionExecutionOutcome.Applied
        };
    }
}
