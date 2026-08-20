using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Globalization;
using System.Windows.Threading;
using FiveMCleaner.App.Services;
using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Core.Planning;

namespace FiveMCleaner.App.ViewModels;

public sealed partial class MainViewModel
{
    public string ReportSummaryLabel { get => reportSummaryLabel; private set => SetProperty(ref reportSummaryLabel, value); }

    public string ReportRestartLabel { get => reportRestartLabel; private set => SetProperty(ref reportRestartLabel, value); }

    public bool IsReportAvailable
    {
        get => isReportAvailable;
        private set
        {
            if (SetProperty(ref isReportAvailable, value))
            {
                OnPropertyChanged(nameof(IsOptimizerIdle));
            }
        }
    }

    public bool IsComparisonAvailable { get => isComparisonAvailable; private set => SetProperty(ref isComparisonAvailable, value); }

    public bool ComparisonRegressionSuspected { get => comparisonRegressionSuspected; private set => SetProperty(ref comparisonRegressionSuspected, value); }

    public string ComparisonSummaryLabel { get => comparisonSummaryLabel; private set => SetProperty(ref comparisonSummaryLabel, value); }

    public string ComparisonHardwareProfileLabel { get => comparisonHardwareProfileLabel; private set => SetProperty(ref comparisonHardwareProfileLabel, value); }

    private void UpsertStepLedgerItem(string actionId, ActionExecutionOutcome outcome)
    {
        var name = GetLocalizedActionName(actionId, actionId);
        var (label, glyph, brushKey) = DescribeOutcome(outcome);
        var item = new StepLedgerItem(actionId, name, outcome, label, glyph, brushKey);
        var existingIndex = -1;
        for (var index = 0; index < StepLedger.Count; index++)
        {
            if (StepLedger[index].ActionId == actionId)
            {
                existingIndex = index;
                break;
            }
        }

        if (existingIndex >= 0)
        {
            StepLedger[existingIndex] = item;
        }
        else
        {
            StepLedger.Add(item);
        }
    }

    private string GetLocalizedActionName(string actionId, string fallback)
    {
        var key = $"Actions.{actionId}.Name";
        var value = localization.GetString(key);
        return value == key ? fallback : value;
    }

    private (string Label, string Glyph, string BrushKey) DescribeOutcome(ActionExecutionOutcome outcome)
    {
        return outcome switch
        {
            ActionExecutionOutcome.Verified => (localization.GetString("Outcome.Verified"), "IconMarkVerified", "InfoBaseBrush"),
            ActionExecutionOutcome.Applied => (localization.GetString("Outcome.Applied"), "IconMarkApplied", "SuccessBaseBrush"),
            ActionExecutionOutcome.Skipped => (localization.GetString("Outcome.Skipped"), "IconMarkSkipped", "NeutralBaseBrush"),
            ActionExecutionOutcome.Warning => (localization.GetString("Outcome.Warning"), "IconMarkWarning", "WarningBaseBrush"),
            ActionExecutionOutcome.Failed => (localization.GetString("Outcome.Failed"), "IconMarkFailed", "DangerBaseBrush"),
            ActionExecutionOutcome.RolledBack => (localization.GetString("Outcome.RolledBack"), "IconMarkRolledBack", "RevertBaseBrush"),
            ActionExecutionOutcome.RollbackFailed => (localization.GetString("Outcome.RollbackFailed"), "IconMarkRollbackFailed", "DangerBaseBrush"),
            ActionExecutionOutcome.NotRun => (localization.GetString("Outcome.NotRun"), "IconMarkNotRun", "TextTertiaryBrush"),
            _ => (localization.GetString("Outcome.Running"), "IconMarkPending", "AccentBrush")
        };
    }

    /// <summary>
    /// Desfecho apresentável do relatório, derivado só de contagens já
    /// existentes em <see cref="OptimizationReportDto"/> — nunca um estado
    /// inventado. A revisão de design pediu quatro tratamentos visuais
    /// distintos (sucesso, sucesso com falhas isoladas, falha, rollback sem
    /// sucesso) e este é o único ponto de decisão para os quatro.
    /// </summary>
    public bool ReportSucceeded => lastReport?.Succeeded ?? false;

    public bool ReportHasRollbackFailures => (lastReport?.RollbackFailedCount ?? 0) > 0;

    public bool ReportHasIsolatedFailures => !ReportSucceeded
        && !ReportHasRollbackFailures
        && (lastReport?.ChangedCount ?? 0) > 0;

    public bool ReportFailedOutright => !ReportSucceeded
        && !ReportHasRollbackFailures
        && (lastReport?.ChangedCount ?? 0) == 0;

    private void ApplyReport(OptimizationReportDto? report)
    {
        lastReport = report;
        IsReportAvailable = report is not null;
        OnPropertyChanged(nameof(CanShareReport));
        OnPropertyChanged(nameof(SuggestedReportFileName));
        OnPropertyChanged(nameof(ReportSucceeded));
        OnPropertyChanged(nameof(ReportHasRollbackFailures));
        OnPropertyChanged(nameof(ReportHasIsolatedFailures));
        OnPropertyChanged(nameof(ReportFailedOutright));
        ReportLines.Clear();
        if (report is null)
        {
            ReportSummaryLabel = string.Empty;
            ReportRestartLabel = string.Empty;
            return;
        }

        ReportSummaryLabel = localization.Format(
            "Report.SummaryFormat",
            report.VerifiedCount,
            report.ChangedCount,
            report.SkippedCount,
            report.WarningCount,
            report.FailedCount);
        ReportRestartLabel = localization.GetString(
            report.RequiresRestart ? "Report.RestartNeeded" : "Report.RestartNotNeeded");

        foreach (var line in report.Lines)
        {
            var (label, glyph, brushKey) = DescribeOutcome(line.Outcome);
            ReportLines.Add(new ReportLineDisplayItem(
                GetLocalizedActionName(line.ActionId, line.ActionName),
                label,
                glyph,
                brushKey,
                line.Reason));
        }
    }

    private void ApplyComparison(OptimizationComparisonResult? comparison)
    {
        lastComparison = comparison;
        IsComparisonAvailable = comparison is not null;
        if (comparison is null)
        {
            ComparisonRegressionSuspected = false;
            ComparisonSummaryLabel = string.Empty;
            ComparisonHardwareProfileLabel = string.Empty;
            OnPropertyChanged(nameof(CanRevertLastOptimization));
            return;
        }

        ComparisonRegressionSuspected = comparison.RegressionSuspected;
        ComparisonSummaryLabel = comparison.RegressionSuspected
            ? localization.GetString("Comparison.RegressionSuspected") + " "
                + string.Join(" ", comparison.RegressionReasons)
            : localization.GetString("Comparison.NoRegression");
        ComparisonHardwareProfileLabel = localization.GetString("Comparison.HardwareProfile")
            + ": " + comparison.HardwareProfileSignature;
        OnPropertyChanged(nameof(CanRevertLastOptimization));
    }

    public bool CanShareReport => lastReport is not null;

    public string SuggestedReportFileName => lastReport is null
        ? "FiveMCleaner-Report.txt"
        : $"FiveMCleaner-Report-{lastReport.TransactionId:N}.txt";

    public void CopyTechnicalReport()
    {
        if (lastReport is null)
        {
            return;
        }

        var text = TechnicalReportBuilder.Build(lastReport, diagnostic, localization);
        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            // Clipboard ownership is best-effort and must not destabilize the UI.
        }
    }

    /// <summary>
    /// Writes the sanitized technical report to a path the user picked
    /// explicitly (via a native save dialog in the code-behind). Never
    /// chooses or guesses a location itself.
    /// </summary>
    public void SaveTechnicalReport(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (lastReport is null)
        {
            return;
        }

        var text = TechnicalReportBuilder.Build(lastReport, diagnostic, localization);
        try
        {
            File.WriteAllText(filePath, text);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            // The caller owns the selected path; a failed export leaves no partial app state.
        }
    }
}
