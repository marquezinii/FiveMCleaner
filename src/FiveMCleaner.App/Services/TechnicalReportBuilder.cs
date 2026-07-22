using System.Text;
using System.Text.RegularExpressions;
using FiveMCleaner.Contracts;

namespace FiveMCleaner.App.Services;

/// <summary>
/// Removes personal identifiers from support text. Replaces real user profile
/// paths with environment tokens so a copied report never leaks the account
/// name. It never emits tokens, entitlement or credential data because those
/// are not part of the report model in the first place.
/// </summary>
public static partial class ReportSanitizer
{
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = text;
        result = ReplaceKnownFolder(result, Environment.SpecialFolder.LocalApplicationData, "%LOCALAPPDATA%");
        result = ReplaceKnownFolder(result, Environment.SpecialFolder.ApplicationData, "%APPDATA%");
        result = ReplaceKnownFolder(result, Environment.SpecialFolder.UserProfile, "%USERPROFILE%");

        // Generic "X:\Users\<name>\" prefix, regardless of the current account.
        result = UsersPathPattern().Replace(result, "%USERPROFILE%\\");
        return result;
    }

    private static string ReplaceKnownFolder(
        string text,
        Environment.SpecialFolder folder,
        string token)
    {
        var path = Environment.GetFolderPath(folder);
        return string.IsNullOrEmpty(path)
            ? text
            : text.Replace(path, token, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"[A-Za-z]:\\Users\\[^\\/:*?""<>|\r\n]+\\", RegexOptions.IgnoreCase)]
    private static partial Regex UsersPathPattern();
}

/// <summary>
/// Builds a plain-text technical report a user can copy for support. The text
/// is sanitized and contains only run outcomes and non-personal system context.
/// </summary>
public static class TechnicalReportBuilder
{
    public static string Build(
        OptimizationReportDto report,
        AppDiagnostic? diagnostic,
        ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(localization);

        var builder = new StringBuilder();
        builder.AppendLine($"{ProductIdentity.Name} — {localization.GetString("Report.Title")}");
        builder.AppendLine($"{localization.GetString("Report.Field.Transaction")}: {report.TransactionId:N}");
        builder.AppendLine(
            $"{localization.GetString("Report.Field.Date")}: {report.CreatedAtUtc.UtcDateTime:yyyy-MM-dd HH:mm} UTC");
        builder.AppendLine($"{localization.GetString("Report.Field.Profile")}: {report.Profile}");
        if (diagnostic is not null)
        {
            builder.AppendLine($"{localization.GetString("Report.Field.System")}: "
                + $"{diagnostic.OsLabel} ({diagnostic.SystemArchitecture})");
        }

        builder.AppendLine();
        builder.AppendLine(
            $"{localization.GetString("Report.Verified")}: {report.VerifiedCount}  "
            + $"{localization.GetString("Report.Changed")}: {report.ChangedCount}  "
            + $"{localization.GetString("Report.Skipped")}: {report.SkippedCount}  "
            + $"{localization.GetString("Report.Warnings")}: {report.WarningCount}  "
            + $"{localization.GetString("Report.Failed")}: {report.FailedCount}");
        builder.AppendLine(
            $"{localization.GetString("Report.RequiresRestart")}: {YesNo(localization, report.RequiresRestart)}  "
            + $"{localization.GetString("Report.RestorePossible")}: {YesNo(localization, report.RestorePossible)}");
        builder.AppendLine();

        foreach (var line in report.Lines)
        {
            builder.AppendLine($"[{OutcomeLabel(localization, line.Outcome)}] {line.ActionName} ({line.ActionId})"
                + (string.IsNullOrWhiteSpace(line.Reason) ? string.Empty : $" — {line.Reason}"));
        }

        return ReportSanitizer.Sanitize(builder.ToString().TrimEnd());
    }

    public static string OutcomeLabel(ILocalizationService localization, ActionExecutionOutcome outcome)
    {
        return localization.GetString(outcome switch
        {
            ActionExecutionOutcome.Verified => "Outcome.Verified",
            ActionExecutionOutcome.Applied => "Outcome.Applied",
            ActionExecutionOutcome.Skipped => "Outcome.Skipped",
            ActionExecutionOutcome.Warning => "Outcome.Warning",
            ActionExecutionOutcome.Failed => "Outcome.Failed",
            ActionExecutionOutcome.RolledBack => "Outcome.RolledBack",
            ActionExecutionOutcome.RollbackFailed => "Outcome.RollbackFailed",
            ActionExecutionOutcome.NotRun => "Outcome.NotRun",
            _ => "Outcome.Running"
        });
    }

    private static string YesNo(ILocalizationService localization, bool value)
    {
        return localization.GetString(value ? "Common.Yes" : "Common.No");
    }
}
