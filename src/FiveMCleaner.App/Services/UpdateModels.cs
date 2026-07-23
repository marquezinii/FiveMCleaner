using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace FiveMCleaner.App.Services;

public interface IReleaseUpdateService
{
    Task<ReleaseUpdate?> CheckForUpdateAsync(
        StableSemanticVersion currentVersion,
        CancellationToken cancellationToken = default);

    Task<DownloadedUpdate> DownloadUpdateAsync(
        ReleaseUpdate update,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A stable SemVer value. Pre-release versions are deliberately not accepted.
/// Build metadata is accepted, but it does not participate in precedence.
/// </summary>
public sealed class StableSemanticVersion : IComparable<StableSemanticVersion>
{
    private const int MaximumTextLength = 128;

    private static readonly Regex StableVersionPattern = new(
        "^(?:v)?(?<major>0|[1-9][0-9]*)\\.(?<minor>0|[1-9][0-9]*)\\.(?<patch>0|[1-9][0-9]*)(?<metadata>\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromMilliseconds(100));

    private StableSemanticVersion(
        BigInteger major,
        BigInteger minor,
        BigInteger patch,
        string? buildMetadata)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        BuildMetadata = buildMetadata;
    }

    public BigInteger Major { get; }

    public BigInteger Minor { get; }

    public BigInteger Patch { get; }

    public string? BuildMetadata { get; }

    public string CoreVersion => string.Create(
        CultureInfo.InvariantCulture,
        $"{Major}.{Minor}.{Patch}");

    public static StableSemanticVersion Parse(string value)
    {
        if (!TryParse(value, out var version))
        {
            throw new FormatException("A versao precisa ser um SemVer estavel no formato X.Y.Z.");
        }

        return version;
    }

    public static StableSemanticVersion FromVersion(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (version.Major < 0 || version.Minor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        return new StableSemanticVersion(
            version.Major,
            version.Minor,
            version.Build < 0 ? 0 : version.Build,
            buildMetadata: null);
    }

    public static bool TryParse(string? value, out StableSemanticVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumTextLength
            || !value.Equals(value.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        var match = StableVersionPattern.Match(value);
        if (!match.Success
            || !BigInteger.TryParse(
                match.Groups["major"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var major)
            || !BigInteger.TryParse(
                match.Groups["minor"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var minor)
            || !BigInteger.TryParse(
                match.Groups["patch"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var patch))
        {
            return false;
        }

        var metadataGroup = match.Groups["metadata"];
        var metadata = metadataGroup.Success
            ? metadataGroup.Value[1..]
            : null;
        version = new StableSemanticVersion(major, minor, patch, metadata);
        return true;
    }

    public int CompareTo(StableSemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var majorComparison = Major.CompareTo(other.Major);
        if (majorComparison != 0)
        {
            return majorComparison;
        }

        var minorComparison = Minor.CompareTo(other.Minor);
        return minorComparison != 0
            ? minorComparison
            : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => BuildMetadata is null
        ? CoreVersion
        : $"{CoreVersion}+{BuildMetadata}";
}

public sealed class ReleaseUpdate
{
    internal ReleaseUpdate(
        StableSemanticVersion version,
        string tagName,
        string assetName,
        Uri downloadUri,
        long sizeBytes,
        string sha256Hex,
        Uri? releaseNotesUri = null)
    {
        Version = version ?? throw new ArgumentNullException(nameof(version));
        TagName = tagName ?? throw new ArgumentNullException(nameof(tagName));
        AssetName = assetName ?? throw new ArgumentNullException(nameof(assetName));
        DownloadUri = downloadUri ?? throw new ArgumentNullException(nameof(downloadUri));
        SizeBytes = sizeBytes;
        Sha256Hex = sha256Hex ?? throw new ArgumentNullException(nameof(sha256Hex));
        ReleaseNotesUri = releaseNotesUri;
    }

    public StableSemanticVersion Version { get; }

    public string TagName { get; }

    public string AssetName { get; }

    public Uri DownloadUri { get; }

    public long SizeBytes { get; }

    public string Sha256Hex { get; }

    /// <summary>
    /// The official GitHub page for the exact release. It is optional for
    /// compatibility with locally constructed values, but API-sourced updates
    /// validate it before exposing it to the interface.
    /// </summary>
    public Uri? ReleaseNotesUri { get; }
}

public sealed record DownloadedUpdate(
    StableSemanticVersion Version,
    string InstallerPath,
    long SizeBytes,
    bool WasAlreadyDownloaded);

public readonly record struct UpdateDownloadProgress(long BytesReceived, long TotalBytes)
{
    public double Percentage => TotalBytes <= 0
        ? 0
        : Math.Clamp(BytesReceived * 100d / TotalBytes, 0, 100);
}

public sealed class GitHubReleaseUpdateOptions
{
    /// <summary>
    /// Exact installer-name template. The {version} token is replaced with the
    /// stable core version from a vX.Y.Z release tag.
    /// </summary>
    public string InstallerAssetNameTemplate { get; init; } =
        "FiveMCleaner-Setup-{version}-win-x64.exe";

    public string? UpdatesRootDirectory { get; init; }

    public long MinimumInstallerSizeBytes { get; init; } = 64 * 1024;

    public long MaximumInstallerSizeBytes { get; init; } = 512L * 1024 * 1024;

    public int MaximumManifestSizeBytes { get; init; } = 1024 * 1024;

    public int MaximumRedirects { get; init; } = 5;

    public TimeSpan ManifestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan DownloadTimeout { get; init; } = TimeSpan.FromMinutes(15);
}

public sealed class UpdateSecurityException : Exception
{
    public UpdateSecurityException(string message)
        : base(message)
    {
    }
}
