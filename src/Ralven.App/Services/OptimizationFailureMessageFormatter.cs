using Ralven.Contracts;

namespace Ralven.App.Services;

/// <summary>
/// Appends "— {localized error code text}" to an existing message when a
/// BugCode was captured. Kept as a pure static function so the composition
/// rule is testable without a WPF/localization host.
/// </summary>
public static class OptimizationFailureMessageFormatter
{
    /// <param name="message">The existing localized message/reason, or null/empty.</param>
    /// <param name="code">The classified failure code, or null when none was captured.</param>
    /// <param name="formatCodeSuffix">
    /// Given the raw code as a string (e.g. "WIN_PRIVILEGE"), returns the
    /// localized "Código do erro: WIN_PRIVILEGE"-style suffix, with no
    /// leading separator — this method supplies the em dash.
    /// </param>
    public static string? AppendCode(string? message, BugCode? code, Func<string, string> formatCodeSuffix)
    {
        if (code is null or BugCode.Unknown)
        {
            return message;
        }

        var suffix = formatCodeSuffix(code.Value.ToString());
        return string.IsNullOrEmpty(message) ? suffix : $"{message} — {suffix}";
    }
}
