using System.Diagnostics;
using System.IO;
using Ralven.Contracts;

namespace Ralven.App.Services;

internal enum ErrorExperienceTestMode
{
    None,
    Recoverable,
    Fatal
}

internal static class ErrorExperienceTest
{
    internal static ErrorExperienceTestMode Parse(IEnumerable<string> arguments, AppRuntimeEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (environment != AppRuntimeEnvironment.Development)
        {
            return ErrorExperienceTestMode.None;
        }

        foreach (var argument in arguments)
        {
            if (argument.Equals("--error-experience-test=recoverable", StringComparison.OrdinalIgnoreCase))
            {
                return ErrorExperienceTestMode.Recoverable;
            }

            if (argument.Equals("--error-experience-test=fatal", StringComparison.OrdinalIgnoreCase))
            {
                return ErrorExperienceTestMode.Fatal;
            }
        }

        return ErrorExperienceTestMode.None;
    }
}

internal static class ErrorDetailsFormatter
{
    // This stays below the bug-report byte limit even for four-byte UTF-8 characters.
    private const int MaximumExceptionTextLength = 24_000;

    internal static string Format(Exception exception, ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(localization);

        var exceptionText = ReportSanitizer.Sanitize(exception.ToString());
        if (exceptionText.Length > MaximumExceptionTextLength)
        {
            exceptionText = exceptionText[..MaximumExceptionTextLength];
        }

        var version = typeof(ErrorDetailsFormatter).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        var details = localization.Format(
            "ErrorDialog.Details.Template",
            ProductIdentity.DisplayName,
            version,
            DateTimeOffset.UtcNow.ToString("O"),
            exception.GetType().FullName ?? exception.GetType().Name,
            exceptionText);
        return ReportSanitizer.Sanitize(details);
    }
}

internal static class AppRestart
{
    internal static bool TryStart(string? executablePath, Func<ProcessStartInfo, Process?>? start = null)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathFullyQualified(executablePath))
        {
            return false;
        }

        try
        {
            var process = (start ?? Process.Start)(new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true
            });
            return process is not null;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            return false;
        }
    }
}
