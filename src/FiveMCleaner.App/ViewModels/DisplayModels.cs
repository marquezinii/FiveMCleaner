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
