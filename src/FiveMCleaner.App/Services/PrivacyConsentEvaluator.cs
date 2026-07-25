namespace FiveMCleaner.App.Services;

/// <summary>
/// Which variant of the privacy consent screen should be presented to the
/// user, or <see cref="AlreadyValid"/> when no screen is needed at all.
/// </summary>
public enum PrivacyConsentScreenVariant
{
    /// <summary>No screen is required: a valid, current consent already exists.</summary>
    AlreadyValid,

    /// <summary>First run: no settings file existed before this evaluation.</summary>
    FirstInstallation,

    /// <summary>
    /// An existing installation that already had a settings file (and, in
    /// practice, may already have decided the older, single telemetry
    /// toggle) but never confirmed a versioned privacy consent — most
    /// commonly because the crash-report toggle is new to this version.
    /// </summary>
    UpgradeFromOlderInstallation,

    /// <summary>
    /// The user had already confirmed a privacy consent version, but a newer
    /// version now exists (a material change to what is collected or where
    /// it is sent) and re-confirmation is required.
    /// </summary>
    ConsentRenewalRequired
}

/// <summary>
/// Typed outcome of evaluating an <see cref="AppSettings"/> snapshot against
/// <see cref="PrivacyConsentPolicy"/>. Carries both whether the consent
/// screen must be shown and whether each kind of transmission is currently
/// authorized to run.
/// </summary>
public sealed record PrivacyConsentDecision
{
    public required bool RequiresConsentScreen { get; init; }

    public required PrivacyConsentScreenVariant Variant { get; init; }

    /// <summary>
    /// True only when <see cref="AppSettings.ShareAnonymousTelemetry"/> is
    /// <see langword="true"/> AND the stored consent version is not older
    /// than <see cref="PrivacyConsentPolicy.CurrentVersion"/>. Both
    /// conditions must hold simultaneously — a checked toggle alone never
    /// authorizes sending anything.
    /// </summary>
    public required bool IsAnonymousTelemetryAuthorized { get; init; }

    /// <summary>
    /// Same rule as <see cref="IsAnonymousTelemetryAuthorized"/>, applied to
    /// <see cref="AppSettings.ShareCrashReports"/>.
    /// </summary>
    public required bool IsCrashReportingAuthorized { get; init; }
}

/// <summary>
/// Pure decision logic for the privacy consent model: given an
/// <see cref="AppSettings"/> snapshot and whether a settings file already
/// existed on disk, decides whether the consent screen must be shown, which
/// variant, and whether each kind of transmission is currently authorized.
/// Deliberately has no UI, disk, network, Cloudflare, or Sentry dependency —
/// callers are responsible for reading settings from disk and for actually
/// showing any screen or sending any data.
/// </summary>
public static class PrivacyConsentEvaluator
{
    /// <param name="settings">
    /// The current, already-loaded settings snapshot to evaluate.
    /// </param>
    /// <param name="settingsFileExistedBeforeLoad">
    /// Whether a settings file already existed on disk before this snapshot
    /// was produced. Distinguishes a brand-new installation (no file at all)
    /// from an older installation that already had a settings file but never
    /// confirmed a versioned consent (<see cref="AppSettings.PrivacyConsentVersion"/>
    /// is <see langword="null"/> in both cases, so this flag is the only way
    /// to tell them apart for the purpose of choosing the screen text).
    /// </param>
    public static PrivacyConsentDecision Evaluate(AppSettings settings, bool settingsFileExistedBeforeLoad)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var storedVersion = settings.PrivacyConsentVersion;
        var requiresRenewal = PrivacyConsentPolicy.RequiresRenewal(storedVersion);

        var variant = !requiresRenewal
            ? PrivacyConsentScreenVariant.AlreadyValid
            : storedVersion is null
                ? settingsFileExistedBeforeLoad
                    ? PrivacyConsentScreenVariant.UpgradeFromOlderInstallation
                    : PrivacyConsentScreenVariant.FirstInstallation
                : PrivacyConsentScreenVariant.ConsentRenewalRequired;

        return new PrivacyConsentDecision
        {
            RequiresConsentScreen = requiresRenewal,
            Variant = variant,
            IsAnonymousTelemetryAuthorized = settings.ShareAnonymousTelemetry && !requiresRenewal,
            IsCrashReportingAuthorized = settings.ShareCrashReports && !requiresRenewal
        };
    }
}
