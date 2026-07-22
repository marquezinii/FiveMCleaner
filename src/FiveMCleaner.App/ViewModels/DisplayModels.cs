using FiveMCleaner.Contracts;

namespace FiveMCleaner.App.ViewModels;

public sealed record ActionDisplayItem(
    string Id,
    string Name,
    string Description,
    string IconGlyph,
    string RiskLabel,
    string PrivilegeLabel);

public sealed record ActivityLogItem(string Time, string Message);

public sealed record HistoryDisplayItem(
    Guid TransactionId,
    string Title,
    string DateLabel,
    string Summary,
    bool CanRollback);

public sealed record StreamingReadinessDisplayItem(
    string IconGlyph,
    string Title,
    string Detail);

/// <summary>One row of the live step ledger shown during optimization.</summary>
public sealed record StepLedgerItem(
    string ActionId,
    string Name,
    ActionExecutionOutcome Outcome,
    string OutcomeLabel,
    string OutcomeGlyph,
    string OutcomeBrushKey);

/// <summary>One line of the final structured report.</summary>
public sealed record ReportLineDisplayItem(
    string ActionName,
    string OutcomeLabel,
    string OutcomeGlyph,
    string OutcomeBrushKey,
    string? Reason);
