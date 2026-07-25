using Sentry;

namespace FiveMCleaner.App.Services;

/// <summary>
/// Real Sentry-backed implementation of <see cref="ICrashReportingService"/>.
/// Every option that could leak more than this product's privacy model
/// allows is explicitly disabled here rather than left at the SDK's default,
/// so a future SDK upgrade changing a default never silently widens what
/// gets sent — see <c>docs/safety.md</c> and <c>docs/telemetry.md</c>.
/// </summary>
public sealed class SentryCrashReportingService : ICrashReportingService
{
    private IDisposable? sdkHandle;

    public bool IsInitialized => sdkHandle is not null;

    public void Initialize(RemoteServicesOptions options, string appVersion)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);

        if (IsInitialized || string.IsNullOrWhiteSpace(options.SentryDsn))
        {
            return;
        }

        sdkHandle = SentrySdk.Init(sentryOptions =>
        {
            sentryOptions.Dsn = options.SentryDsn;
            sentryOptions.Environment = options.Environment;
            sentryOptions.Release = $"fivemcleaner@{appVersion}";

            // Never send IP, machine name, or any other automatically
            // collected personal/technical identifier beyond the sanitized
            // exception itself.
            sentryOptions.SendDefaultPii = false;
            sentryOptions.IsEnvironmentUser = false;
            sentryOptions.AttachStacktrace = true;

            // No session pings, no HTTP breadcrumbs/failed-request capture,
            // no performance tracing — only the sanitized error event itself
            // is ever transmitted, matching the closed allowlist already
            // documented for anonymous telemetry.
            sentryOptions.AutoSessionTracking = false;
            sentryOptions.CaptureFailedRequests = false;
            sentryOptions.TracesSampleRate = 0.0;

            sentryOptions.SetBeforeSend(CrashReportSanitizer.Sanitize);
        });
    }

    public void CaptureException(Exception exception)
    {
        if (IsInitialized)
        {
            SentrySdk.CaptureException(exception);
        }
    }

    public void Shutdown()
    {
        sdkHandle?.Dispose();
        sdkHandle = null;
    }
}
