using FiveMCleaner.App.Services;

namespace FiveMCleaner.Tests.App;

/// <summary>
/// Test double for <see cref="IReleaseUpdateService"/> letting tests control
/// exactly what <see cref="MainViewModel.CheckForUpdatesManuallyAsync"/> and
/// <see cref="MainViewModel.CheckForUpdatesAsync"/> observe, without any real
/// network access.
/// </summary>
internal sealed class FakeReleaseUpdateService : IReleaseUpdateService
{
    private readonly ReleaseUpdate? updateToReturn;
    private readonly Exception? exceptionToThrow;

    public FakeReleaseUpdateService(ReleaseUpdate? updateToReturn = null, Exception? exceptionToThrow = null)
    {
        this.updateToReturn = updateToReturn;
        this.exceptionToThrow = exceptionToThrow;
    }

    public int CheckForUpdateCallCount { get; private set; }

    public static ReleaseUpdate CreateUpdate(string version = "9.9.9") => new(
        StableSemanticVersion.Parse(version),
        tagName: $"v{version}",
        assetName: $"FiveMCleaner-Setup-{version}-win-x64.exe",
        downloadUri: new Uri($"https://example.com/FiveMCleaner-Setup-{version}-win-x64.exe"),
        sizeBytes: 10 * 1024 * 1024,
        sha256Hex: new string('a', 64),
        releaseNotesUri: new Uri("https://example.com/releases/tag/v" + version));

    public Task<ReleaseUpdate?> CheckForUpdateAsync(
        StableSemanticVersion currentVersion,
        CancellationToken cancellationToken = default)
    {
        CheckForUpdateCallCount++;
        return exceptionToThrow is not null
            ? Task.FromException<ReleaseUpdate?>(exceptionToThrow)
            : Task.FromResult(updateToReturn);
    }

    public Task<DownloadedUpdate> DownloadUpdateAsync(
        ReleaseUpdate update,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by these tests.");
}
