namespace FiveMCleaner.App.Services;

/// <summary>
/// Typed outcome of evaluating an <see cref="AppSettings"/> snapshot against
/// <see cref="ReleaseNotesCatalog"/> for the currently running app version.
/// </summary>
public sealed record ReleaseNotesDecision
{
    /// <summary>
    /// True when the panel should be shown for <see cref="Entry"/>. The
    /// caller is responsible for persisting the outcome (see
    /// <see cref="MainViewModel.ConfirmReleaseNotesSeenAsync"/>) only after
    /// the user actually closes the panel, so a crash or forced quit before
    /// that point does not silently mark unseen notes as seen.
    /// </summary>
    public required bool ShouldShow { get; init; }

    /// <summary>
    /// True when nothing should be shown, but the current version should
    /// still be recorded as the new baseline right away — a brand-new
    /// installation (nothing to compare against yet) or an upgrade to a
    /// version that has no catalog entry (nothing to show, but the version
    /// was still "seen" by virtue of running it).
    /// </summary>
    public required bool ShouldRecordSilently { get; init; }

    /// <summary>Non-null only when <see cref="ShouldShow"/> is true.</summary>
    public ReleaseNoteVersion? Entry { get; init; }
}

/// <summary>
/// Pure decision logic: given an <see cref="AppSettings"/> snapshot, whether
/// a settings file already existed on disk, the version currently running,
/// and the release notes catalog, decides whether the "What's New" panel
/// must be shown and for which version. Deliberately has no UI, disk, or
/// version-reading dependency of its own — callers pass in an already
/// formatted version string (see <see cref="MainViewModel.AppVersion"/>) and
/// are responsible for actually showing any window or saving any settings.
/// Mirrors <see cref="PrivacyConsentEvaluator"/>, the established pattern in
/// this codebase for "show a screen at most once per version".
/// </summary>
public static class ReleaseNotesEvaluator
{
    /// <param name="settings">The current, already-loaded settings snapshot.</param>
    /// <param name="settingsFileExistedBeforeLoad">
    /// Whether a settings file already existed on disk before this snapshot
    /// was produced. Distinguishes a brand-new installation (nothing to
    /// compare against, so the panel must stay quiet on first run) from an
    /// existing installation upgrading into the first version that has this
    /// feature (<see cref="AppSettings.LastSeenReleaseNotesVersion"/> is
    /// <see langword="null"/> in both cases, so this flag is the only way to
    /// tell them apart).
    /// </param>
    /// <param name="currentVersion">
    /// The app version currently running, formatted like
    /// <see cref="MainViewModel.AppVersion"/> (for example "1.4.0").
    /// </param>
    /// <param name="catalog">
    /// The release notes catalog to look up <paramref name="currentVersion"/>
    /// in — normally <see cref="ReleaseNotesCatalog.Versions"/>, passed
    /// explicitly so this method stays a pure function of its inputs and is
    /// directly unit testable with fake catalogs.
    /// </param>
    public static ReleaseNotesDecision Evaluate(
        AppSettings settings,
        bool settingsFileExistedBeforeLoad,
        string currentVersion,
        IReadOnlyList<ReleaseNoteVersion> catalog)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalog);

        if (!settingsFileExistedBeforeLoad)
        {
            // Brand-new installation: the user is already on the latest
            // version by definition, so there is nothing "new" to tell them.
            // Record the baseline quietly so a future upgrade compares
            // correctly.
            return new ReleaseNotesDecision { ShouldShow = false, ShouldRecordSilently = true };
        }

        if (!StableSemanticVersion.TryParse(currentVersion, out var current))
        {
            return new ReleaseNotesDecision { ShouldShow = false, ShouldRecordSilently = false };
        }

        if (settings.LastSeenReleaseNotesVersion is { } lastSeenRaw
            && StableSemanticVersion.TryParse(lastSeenRaw, out var lastSeen)
            && current.CompareTo(lastSeen) <= 0)
        {
            // Same version already seen, or an older version running after a
            // downgrade/rollback — never show notes going backwards in time.
            return new ReleaseNotesDecision { ShouldShow = false, ShouldRecordSilently = false };
        }

        var entry = catalog.FirstOrDefault(candidate => candidate.Version == currentVersion);
        return entry is null
            ? new ReleaseNotesDecision { ShouldShow = false, ShouldRecordSilently = true }
            : new ReleaseNotesDecision { ShouldShow = true, ShouldRecordSilently = false, Entry = entry };
    }
}
