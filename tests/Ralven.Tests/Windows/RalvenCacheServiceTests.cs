using Ralven.Windows.Infrastructure;
using Xunit;

namespace Ralven.Tests.Windows;

public sealed class RalvenCacheServiceTests
{
    [Fact]
    public async Task InspectAndClean_ClassifyAllowlistedFilesAndPreservePersistentData()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.Combine("Ralven");
        var legacy = temporary.Combine("Vemryx One");
        Write(root, "Cache", "response.bin", 11);
        Write(root, "Temp", "scratch.tmp", 12);
        Write(root, "Requests", "orphan.json", 13);
        Write(root, "Logs", "crash.log", 14);
        Write(root, "Updates", "1.0.0", "update.zip", 15);
        Write(root, "Telemetry", "pending", "telemetry_failures.log", 16);
        Write(root, "Telemetry", "pending", "queued-event.json", 101);
        Write(root, "UpdaterTelemetry", "pending", "queued-event.json", 102);
        Write(root, "Personal", "workspace-deadbeef.tmp", 17);
        Write(root, "Personal", "workspace.json", 103);
        Write(root, "avatars", "user.png.deadbeef.tmp", 18);
        Write(root, "avatars", "user.png", 104);
        Write(root, "Updater", "Ralven.Updater.exe.deadbeef.new", 19);
        Write(root, "Updater", "Ralven.Updater.exe", 105);
        Write(root, ".settings.deadbeef.tmp", 20);
        Write(root, ".application-update-ignores.deadbeef.tmp", 22);
        Write(root, "settings.json.deadbeef.importing", 23);
        Write(root, "firebase.session.deadbeef.importing", 24);
        Write(root, "history.json.deadbeef.importing", 25);
        Write(root, "settings.json.importing.backup", 113);
        Write(root, "unrelated.json.deadbeef.importing", 114);
        Write(root, "settings.json", 106);
        Write(root, "firebase.session", 107);
        Write(root, "application-update-ignores.json", 108);
        Write(root, "Transactions", "transaction.json", 109);
        Write(root, "AuthQuarantine", "receipt.json", 110);
        Write(root, "UpdateSecurity", "version-floor.dpapi", 111);
        Write(legacy, "Logs", "legacy.log", 21);
        Write(legacy, "history.json", 112);

        var service = new RalvenCacheService(root, [legacy]);
        var snapshot = await service.InspectAsync();

        Assert.Equal(270, snapshot.TotalSizeBytes);
        Assert.Equal(15, snapshot.FileCount);
        Assert.Equal(11, Usage(snapshot, RalvenCacheCategory.ApplicationCache).SizeBytes);
        Assert.Equal(193, Usage(snapshot, RalvenCacheCategory.TemporaryFiles).SizeBytes);
        Assert.Equal(51, Usage(snapshot, RalvenCacheCategory.Logs).SizeBytes);
        Assert.Equal(15, Usage(snapshot, RalvenCacheCategory.UpdateDownloads).SizeBytes);

        var result = await service.CleanAsync();

        Assert.True(result.IsComplete);
        Assert.Equal(snapshot.TotalSizeBytes, result.DeletedBytes);
        Assert.Equal(snapshot.FileCount, result.DeletedFileCount);
        Assert.Equal(0, result.After.TotalSizeBytes);
        foreach (var path in new[]
        {
            Path.Combine(root, "settings.json"),
            Path.Combine(root, "firebase.session"),
            Path.Combine(root, "application-update-ignores.json"),
            Path.Combine(root, "Telemetry", "pending", "queued-event.json"),
            Path.Combine(root, "UpdaterTelemetry", "pending", "queued-event.json"),
            Path.Combine(root, "Personal", "workspace.json"),
            Path.Combine(root, "avatars", "user.png"),
            Path.Combine(root, "Updater", "Ralven.Updater.exe"),
            Path.Combine(root, "settings.json.importing.backup"),
            Path.Combine(root, "unrelated.json.deadbeef.importing"),
            Path.Combine(root, "Transactions", "transaction.json"),
            Path.Combine(root, "AuthQuarantine", "receipt.json"),
            Path.Combine(root, "UpdateSecurity", "version-floor.dpapi"),
            Path.Combine(legacy, "history.json")
        })
        {
            Assert.True(File.Exists(path), $"Persistent file was removed: {path}");
        }

        var repeated = await service.CleanAsync();
        Assert.True(repeated.IsComplete);
        Assert.Equal(0, repeated.DeletedBytes);
        Assert.Equal(0, repeated.DeletedFileCount);
    }

    [Fact]
    public async Task Clean_ReportsLockedFileAsPartialAndRetriesAfterRelease()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.Combine("Ralven");
        var locked = Write(root, "Updates", "locked.zip", 32);
        Write(root, "Logs", "discardable.log", 16);
        var service = new RalvenCacheService(root);

        RalvenCacheCleanupResult first;
        using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            first = await service.CleanAsync();
            Assert.False(first.IsComplete);
            Assert.Equal(1, first.FailedFileCount);
            Assert.Equal(16, first.DeletedBytes);
            Assert.True(File.Exists(locked));
        }

        var retry = await service.CleanAsync();
        Assert.True(retry.IsComplete);
        Assert.Equal(32, retry.DeletedBytes);
        Assert.False(File.Exists(locked));
    }

    [Fact]
    public async Task Clean_DoesNotFollowReparsePointsOutsideTheAllowlist()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.Combine("Ralven");
        var cache = Path.Combine(root, "Cache");
        var outside = temporary.Combine("outside");
        Directory.CreateDirectory(cache);
        Directory.CreateDirectory(outside);
        var outsideFile = Write(outside, "keep.txt", 23);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(cache, "outside-link"), outside);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException
            or PlatformNotSupportedException)
        {
            return;
        }

        var result = await new RalvenCacheService(root).CleanAsync();

        Assert.False(result.IsComplete);
        Assert.True(result.After.SkippedPathCount > 0);
        Assert.True(File.Exists(outsideFile));
    }

    [Fact]
    public async Task Clean_ReportsReadOnlyFilesWithoutRemovingOtherCache()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.Combine("Ralven");
        var readOnly = Write(root, "Logs", "read-only.log", 8);
        Write(root, "Logs", "normal.log", 4);
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        try
        {
            var result = await new RalvenCacheService(root).CleanAsync();

            Assert.False(result.IsComplete);
            Assert.Equal(1, result.FailedFileCount);
            Assert.Equal(4, result.DeletedBytes);
            Assert.True(File.Exists(readOnly));
        }
        finally
        {
            File.SetAttributes(readOnly, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Constructor_RejectsRelativeDataRoot()
    {
        Assert.Throws<ArgumentException>(() => new RalvenCacheService("relative"));
    }

    private static RalvenCacheCategoryUsage Usage(
        RalvenCacheSnapshot snapshot,
        RalvenCacheCategory category) => snapshot.Categories.Single(item => item.Category == category);

    private static string Write(string root, params object[] partsAndLength)
    {
        var length = Assert.IsType<int>(partsAndLength[^1]);
        var parts = partsAndLength[..^1].Cast<string>().ToArray();
        var path = parts.Aggregate(root, (current, part) => Path.Combine(current, part));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[length]);
        return path;
    }
}
