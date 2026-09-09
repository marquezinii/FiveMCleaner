namespace Ralven.UpdateRuntime;

/// <summary>
/// Stable, privacy-safe identifiers for updater diagnostics sent to the
/// dashboard. Values are append-only: once released, a code keeps its meaning.
/// </summary>
public static class UpdaterEventCodes
{
    public const string HealthConfirmed = "u000";

    public const string ManifestSourceRejected = "u101";
    public const string ManifestTooLarge = "u102";
    public const string ManifestSchemaInvalid = "u103";
    public const string ManifestTrustInvalid = "u104";

    public const string PackageSourceRejected = "u201";
    public const string PackageRedirectRejected = "u202";
    public const string PackageResponseRejected = "u203";
    public const string PackageSizeMismatch = "u204";
    public const string PackageHashMismatch = "u205";

    public const string StagingFailed = "u301";
    public const string StagingIntegrityFailed = "u302";
    public const string InstallerPathRejected = "u303";
    public const string InstallerMetadataInvalid = "u304";
    public const string UpdaterCopyIntegrityFailed = "u305";

    public const string ActivationFailed = "u401";
    public const string LauncherStartFailed = "u402";
    public const string ParentExitTimeout = "u403";
    public const string ActiveRuntimeInvalid = "u404";

    public const string HealthCheckTimeout = "u501";
    public const string RollbackFailed = "u502";

    public const string AccessDenied = "u601";
    public const string LocalIoFailed = "u602";
    public const string NetworkFailed = "u701";
    public const string RequestTimedOut = "u702";
    public const string Unexpected = "u999";
}
