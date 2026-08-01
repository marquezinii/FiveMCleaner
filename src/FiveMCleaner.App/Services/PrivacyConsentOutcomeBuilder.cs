namespace FiveMCleaner.App.Services;

/// <summary>
/// Pure logic that turns the privacy choices confirmed with "Continue" into
/// the <see cref="AppSettings"/> snapshot that should be persisted. Has no
/// UI, disk, or network dependency, so it is directly unit testable; callers
/// are responsible for saving it through
/// <see cref="IAppOptimizationService.SaveSettingsAsync"/>.
/// </summary>
public static class PrivacyConsentOutcomeBuilder
{
    /// <summary>
    /// Applies the user's checkbox choices and stamps the current consent
    /// version. Every other field is copied unchanged from
    /// <paramref name="current"/>.
    /// </summary>
    public static AppSettings BuildConfirmed(
        AppSettings current,
        bool acceptAnonymousTelemetry,
        bool acceptCrashReports)
    {
        ArgumentNullException.ThrowIfNull(current);
        return current with
        {
            ShareAnonymousTelemetry = acceptAnonymousTelemetry,
            ShareCrashReports = acceptCrashReports,
            PrivacyConsentVersion = PrivacyConsentPolicy.CurrentVersion
        };
    }
}
