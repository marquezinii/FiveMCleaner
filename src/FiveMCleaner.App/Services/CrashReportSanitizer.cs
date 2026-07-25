using Sentry;

namespace FiveMCleaner.App.Services;

/// <summary>
/// Sanitizes a Sentry event before it leaves the process, reusing the same
/// path-scrubbing rules already applied to the copyable technical report
/// (<see cref="ReportSanitizer"/>) so the two privacy guarantees never
/// diverge. Also strips anything Sentry might auto-populate that this
/// product's privacy model never allows: machine identifiers and user data.
/// </summary>
public static class CrashReportSanitizer
{
    private const string NonIdentifyingServerName = "fivemcleaner-client";

    public static SentryEvent Sanitize(SentryEvent sentryEvent)
    {
        ArgumentNullException.ThrowIfNull(sentryEvent);

        // Never let Sentry's own auto-detection attach the real machine name
        // or any user identity — this product's telemetry model never sends
        // either, regardless of what the SDK would otherwise capture.
        sentryEvent.ServerName = NonIdentifyingServerName;
        sentryEvent.User.Id = null;
        sentryEvent.User.Username = null;
        sentryEvent.User.Email = null;
        sentryEvent.User.IpAddress = null;

        if (sentryEvent.Message is { } message)
        {
            message.Message = ReportSanitizer.Sanitize(message.Message);
            message.Formatted = ReportSanitizer.Sanitize(message.Formatted);
        }

        foreach (var sentryException in sentryEvent.SentryExceptions ?? [])
        {
            sentryException.Value = ReportSanitizer.Sanitize(sentryException.Value);
            SanitizeStackTrace(sentryException.Stacktrace);
        }

        SanitizeStackTrace(sentryEvent.SentryThreads?
            .Select(thread => thread.Stacktrace)
            .FirstOrDefault(stackTrace => stackTrace is not null));

        return sentryEvent;
    }

    private static void SanitizeStackTrace(SentryStackTrace? stackTrace)
    {
        if (stackTrace is null)
        {
            return;
        }

        foreach (var frame in stackTrace.Frames)
        {
            frame.FileName = ReportSanitizer.Sanitize(frame.FileName);
            frame.AbsolutePath = ReportSanitizer.Sanitize(frame.AbsolutePath);
        }
    }
}
