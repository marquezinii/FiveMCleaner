using FiveMCleaner.Contracts;

namespace FiveMCleaner.App.ViewModels;

public sealed record ActionDisplayItem(
    string Id,
    string Name,
    string Description,
    string IconGlyph,
    string RiskLabel,
    string RiskBrushKey,
    string PrivilegeLabel,
    bool RequiresElevation,
    string CategoryLabel);

public sealed record ActivityLogItem(string Time, string Message);

public sealed record HistoryDisplayItem(
    Guid TransactionId,
    string Title,
    string DateLabel,
    string Summary,
    bool CanRollback);

/// <summary>
/// One streaming-readiness check on the overview. <paramref name="IconKey"/> is
/// the resource key of a vector icon declared in <c>Themes/Icons.xaml</c>, not a
/// font glyph, so the icon follows the same stroke and color as the rest of the
/// page.
/// </summary>
public sealed record StreamingReadinessDisplayItem(
    string IconKey,
    string Title,
    string Detail,
    string ToneBrushKey);

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
