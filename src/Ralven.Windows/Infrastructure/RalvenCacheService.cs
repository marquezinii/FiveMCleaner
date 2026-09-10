using Ralven.Contracts;

namespace Ralven.Windows.Infrastructure;

public enum RalvenCacheCategory
{
    ApplicationCache,
    TemporaryFiles,
    Logs,
    UpdateDownloads
}

public sealed record RalvenCacheCategoryUsage(
    RalvenCacheCategory Category,
    long SizeBytes,
    int FileCount);

public sealed record RalvenCacheSnapshot(
    IReadOnlyList<RalvenCacheCategoryUsage> Categories,
    int SkippedPathCount)
{
    public long TotalSizeBytes => Categories.Sum(category => category.SizeBytes);

    public int FileCount => Categories.Sum(category => category.FileCount);
}

public sealed record RalvenCacheCleanupResult(
    RalvenCacheSnapshot Before,
    RalvenCacheSnapshot After,
    long DeletedBytes,
    int DeletedFileCount,
    int FailedFileCount)
{
    public bool IsComplete => FailedFileCount == 0
        && After.FileCount == 0
        && After.SkippedPathCount == 0;
}

/// <summary>
/// Inventories and removes only regenerable Ralven data. Persistent settings,
/// authentication, user files, telemetry queues, journals and update security
/// state are intentionally outside the allowlist below.
/// </summary>
public sealed class RalvenCacheService
{
    private static readonly string[] LegacyProductNames = ["Vemryx One", "FiveMCleaner"];
    private readonly string dataRoot;
    private readonly IReadOnlyList<string> legacyRoots;
    private readonly SafeFileTree fileTree = new();
    private readonly SemaphoreSlim gate = new(1, 1);

    public RalvenCacheService()
        : this(DefaultDataRoot(), DefaultLegacyRoots())
    {
    }

    internal RalvenCacheService(string dataRoot, IEnumerable<string>? legacyRoots = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        if (!Path.IsPathFullyQualified(dataRoot))
        {
            throw new ArgumentException("The Ralven data root must be absolute.", nameof(dataRoot));
        }

        this.dataRoot = SafePath.Normalize(dataRoot);
        this.legacyRoots = (legacyRoots ?? [])
            .Select(SafePath.Normalize)
            .Where(root => !root.Equals(this.dataRoot, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<RalvenCacheSnapshot> InspectAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Scan(cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RalvenCacheCleanupResult> CleanAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Clean(cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private RalvenCacheCleanupResult Clean(CancellationToken cancellationToken)
    {
        var beforeScan = Collect(cancellationToken);
        long deletedBytes = 0;
        var deletedFiles = 0;
        var failedFiles = 0;

        foreach (var file in beforeScan.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                SafePath.EnsureDescendant(file.SourceRoot, file.Entry.FullPath);
                SafePath.EnsureNoReparsePoints(file.Entry.FullPath);
                File.Delete(file.Entry.FullPath);
                if (File.Exists(file.Entry.FullPath))
                {
                    failedFiles++;
                    continue;
                }

                deletedBytes = checked(deletedBytes + file.Entry.Length);
                deletedFiles++;
            }
            catch (FileNotFoundException)
            {
                deletedBytes = checked(deletedBytes + file.Entry.Length);
                deletedFiles++;
            }
            catch (Exception exception) when (IsExpectedFileSystemFailure(exception))
            {
                failedFiles++;
            }
        }

        return new RalvenCacheCleanupResult(
            Snapshot(beforeScan),
            Scan(cancellationToken),
            deletedBytes,
            deletedFiles,
            failedFiles);
    }

    private RalvenCacheSnapshot Scan(CancellationToken cancellationToken) => Snapshot(Collect(cancellationToken));

    private CacheScan Collect(CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, CacheFile>(StringComparer.OrdinalIgnoreCase);
        var skippedPaths = 0;

        foreach (var root in new[] { dataRoot }.Concat(legacyRoots))
        {
            AddDirectory(Path.Combine(root, "Cache"), RalvenCacheCategory.ApplicationCache, _ => true);
            AddDirectory(Path.Combine(root, "Temp"), RalvenCacheCategory.TemporaryFiles, _ => true);
            AddDirectory(Path.Combine(root, "Requests"), RalvenCacheCategory.TemporaryFiles, _ => true);
            AddDirectory(Path.Combine(root, "Logs"), RalvenCacheCategory.Logs, _ => true);
            AddDirectory(Path.Combine(root, "Updates"), RalvenCacheCategory.UpdateDownloads, _ => true);
            AddDirectory(Path.Combine(root, "Telemetry", "pending"), RalvenCacheCategory.Logs,
                entry => Path.GetFileName(entry.FullPath).Equals("telemetry_failures.log", StringComparison.OrdinalIgnoreCase));
            AddDirectory(Path.Combine(root, "Personal"), RalvenCacheCategory.TemporaryFiles,
                entry => Path.GetFileName(entry.FullPath).StartsWith("workspace-", StringComparison.OrdinalIgnoreCase)
                    && entry.FullPath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
            AddDirectory(Path.Combine(root, "avatars"), RalvenCacheCategory.TemporaryFiles,
                entry => entry.FullPath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
            AddDirectory(Path.Combine(root, "Updater"), RalvenCacheCategory.TemporaryFiles,
                entry => Path.GetFileName(entry.FullPath).StartsWith("Ralven.Updater.exe.", StringComparison.OrdinalIgnoreCase)
                    && entry.FullPath.EndsWith(".new", StringComparison.OrdinalIgnoreCase));
            AddTopLevelTemporaryFiles(root);
        }

        return new CacheScan(files.Values.OrderBy(file => file.Entry.FullPath, StringComparer.OrdinalIgnoreCase).ToArray(), skippedPaths);

        void AddDirectory(
            string sourceRoot,
            RalvenCacheCategory category,
            Func<SafeFileEntry, bool> predicate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = fileTree.EnumerateFiles(sourceRoot, predicate);
                skippedPaths += result.SkippedReparsePoints.Count + result.SkippedInaccessiblePaths.Count;
                foreach (var entry in result.Files)
                {
                    files[entry.FullPath] = new CacheFile(SafePath.Normalize(sourceRoot), entry, category);
                }
            }
            catch (Exception exception) when (IsExpectedFileSystemFailure(exception))
            {
                skippedPaths++;
            }
        }

        void AddTopLevelTemporaryFiles(string root)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(root))
            {
                return;
            }

            try
            {
                SafePath.EnsureNoReparsePoints(root);
                foreach (var file in new DirectoryInfo(root).EnumerateFiles())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        skippedPaths++;
                        continue;
                    }

                    if (!IsKnownTopLevelTemporaryFile(file.Name))
                    {
                        continue;
                    }

                    var fullPath = SafePath.EnsureDescendant(root, file.FullName);
                    files[fullPath] = new CacheFile(
                        SafePath.Normalize(root),
                        new SafeFileEntry(
                            fullPath,
                            file.Name,
                            file.Length,
                            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)),
                        RalvenCacheCategory.TemporaryFiles);
                }
            }
            catch (Exception exception) when (IsExpectedFileSystemFailure(exception))
            {
                skippedPaths++;
            }
        }
    }

    private static RalvenCacheSnapshot Snapshot(CacheScan scan)
    {
        var categories = Enum.GetValues<RalvenCacheCategory>()
            .Select(category =>
            {
                var files = scan.Files.Where(file => file.Category == category).ToArray();
                return new RalvenCacheCategoryUsage(
                    category,
                    files.Sum(file => file.Entry.Length),
                    files.Length);
            })
            .ToArray();
        return new RalvenCacheSnapshot(categories, scan.SkippedPathCount);
    }

    private static bool IsKnownTopLevelTemporaryFile(string name) =>
        ((name.StartsWith(".settings.", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(".application-update-ignores.", StringComparison.OrdinalIgnoreCase))
            && name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        || ((name.StartsWith("settings.json.", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("firebase.session.", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("history.json.", StringComparison.OrdinalIgnoreCase))
            && name.EndsWith(".importing", StringComparison.OrdinalIgnoreCase));

    private static bool IsExpectedFileSystemFailure(Exception exception) => exception is
        IOException
        or UnauthorizedAccessException
        or NotSupportedException
        or System.Security.SecurityException
        or InvalidOperationException;

    private static string DefaultDataRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ProductIdentity.Name);

    private static IEnumerable<string> DefaultLegacyRoots()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return LegacyProductNames.Select(name => Path.Combine(localAppData, name));
    }

    private sealed record CacheFile(
        string SourceRoot,
        SafeFileEntry Entry,
        RalvenCacheCategory Category);

    private sealed record CacheScan(
        IReadOnlyList<CacheFile> Files,
        int SkippedPathCount);
}
