namespace FiveMCleaner.App.Services;

/// <summary>
/// Pure logic that turns a privacy consent decision — the user's checkbox
/// choices on "Continue", or an implicit decline from closing the consent
/// window — into the <see cref="AppSettings"/> snapshot that should be
/// persisted. Has no UI, disk, or network dependency, so it is directly unit
/// testable; callers are responsible for actually saving the result through
/// the existing settings persistence mechanism
/// (<see cref="IAppOptimizationService.SaveSettingsAsync"/>).
/// </summary>
public static class PrivacyConsentOutcomeBuilder
{
    /// <summary>
    /// Builds the settings to persist when the user clicks "Continue":
    /// applies their checkbox choices and stamps the current consent
    /// version. Every other field is copied unchanged from
    /// <paramref name="current"/> — existing preferences are never reset.
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

    /// <summary>
    /// Builds the settings to persist when the user closes the consent
    /// window without clicking "Continue" (the title bar close button or
    /// Alt+F4): both toggles are treated as declined, but the current
    /// consent version is still recorded so the screen does not reappear on
    /// every launch. Equivalent to
    /// <see cref="BuildConfirmed(AppSettings, bool, bool)"/> with both
    /// choices set to <see langword="false"/> — kept as a separate, named
    /// entry point so the "closing means declining" rule reads explicitly at
    /// call sites.
    /// </summary>
    public static AppSettings BuildDeclinedByClosing(AppSettings current) =>
        BuildConfirmed(current, acceptAnonymousTelemetry: false, acceptCrashReports: false);
}
