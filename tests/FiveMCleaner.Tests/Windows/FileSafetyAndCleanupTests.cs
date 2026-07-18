using FiveMCleaner.Contracts;
using FiveMCleaner.Windows.Actions;
using FiveMCleaner.Windows.Infrastructure;
using Xunit;

namespace FiveMCleaner.Tests.Windows;

public sealed class FileSafetyAndCleanupTests
{
    [Theory]
    [InlineData("FiveM", true)]
    [InlineData("FiveM_b3258_GTAProcess", true)]
    [InlineData("CitizenFX_SubProcess", true)]
    [InlineData("FiveMCleaner", false)]
    [InlineData("FiveMCleaner.Broker", false)]
    [InlineData("MyFiveMTool", false)]
    [InlineData("Discord", false)]
    public void ProcessNameFallback_IsConservativeWhenImagePathIsUnavailable(
        string processName,
        bool expected)
    {
        Assert.Equal(
            expected,
            WindowsFiveMProcessInspector.LooksLikeFiveMProcessName(processName));
    }

    [Fact]
    public void EnsureDescendant_RejectsSiblingWithSharedPrefix()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var root = temporaryDirectory.Combine("root");
        var sibling = temporaryDirectory.Combine("root-escape", "file.txt");

        Assert.Throws<InvalidOperationException>(() =>
            SafePath.EnsureDescendant(root, sibling));
    }

    [Fact]
    public void EnumerateFiles_DoesNotFollowDirectoryReparsePoints()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var root = temporaryDirectory.Combine("root");
        var outside = temporaryDirectory.Combine("outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(root, "inside.txt"), "inside");
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "secret");
        var link = Path.Combine(root, "linked-outside");
        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException
            or PlatformNotSupportedException)
        {
            return;
        }

        var result = new SafeFileTree().EnumerateFiles(root, _ => true);

        Assert.Equal(["inside.txt"], result.Files.Select(file => file.RelativePath));
        Assert.Contains(result.SkippedReparsePoints, path =>
            path.Equals(link, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnumerateFiles_RejectsAReparsePointInAnAncestor()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var realRoot = temporaryDirectory.Combine("real-root");
        var nested = Path.Combine(realRoot, "nested");
        var link = temporaryDirectory.Combine("linked-root");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "outside.txt"), "outside");
        try
        {
            Directory.CreateSymbolicLink(link, realRoot);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException
            or PlatformNotSupportedException)
        {
            return;
        }

        Assert.Throws<IOException>(() => new SafeFileTree().EnumerateFiles(
            Path.Combine(link, "nested"),
            _ => true));
    }

    [Fact]
    public async Task UserTemporaryCleanup_DeletesOnlyOldFilesAfterCommit()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var tempRoot = temporaryDirectory.Combine("temp");
        Directory.CreateDirectory(tempRoot);
        var oldFile = Path.Combine(tempRoot, "old.tmp");
        var newFile = Path.Combine(tempRoot, "new.tmp");
        var unrelatedEmptyDirectory = Path.Combine(tempRoot, "unrelated-empty");
        Directory.CreateDirectory(unrelatedEmptyDirectory);
        File.WriteAllText(oldFile, "old");
        File.WriteAllText(newFile, "new");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-10));
        File.SetLastWriteTimeUtc(newFile, DateTime.UtcNow);
        var action = new UserTemporaryFilesCleanupAction(
            tempRoot,
            TimeSpan.FromDays(7));
        var context = Context();

        var result = await action.ApplyAsync(context, CancellationToken.None);

        Assert.True(result.Changed);
        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(newFile));
        await action.CommitAsync(context, result.SnapshotJson, CancellationToken.None);
        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(newFile));
        Assert.True(Directory.Exists(unrelatedEmptyDirectory));
    }

    [Fact]
    public async Task UserTemporaryCleanup_RollbackRestoresQuarantinedFiles()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var tempRoot = temporaryDirectory.Combine("temp");
        Directory.CreateDirectory(tempRoot);
        var oldFile = Path.Combine(tempRoot, "old.tmp");
        File.WriteAllText(oldFile, "original");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-10));
        var action = new UserTemporaryFilesCleanupAction(
            tempRoot,
            TimeSpan.FromDays(7));
        var context = Context();
        var result = await action.ApplyAsync(context, CancellationToken.None);

        await action.RollbackAsync(context, result.SnapshotJson, CancellationToken.None);

        Assert.Equal("original", File.ReadAllText(oldFile));
    }

    [Fact]
    public async Task ServerCacheRepair_PreservesProtectedFiveMData()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var installation = temporaryDirectory.Combine("FiveM");
        var app = Path.Combine(installation, "FiveM.app");
        var data = Path.Combine(app, "data");
        var serverCache = Path.Combine(data, "server-cache");
        var privateCache = Path.Combine(data, "server-cache-priv");
        var gameStorage = Path.Combine(data, "game-storage");
        var nuiStorage = Path.Combine(data, "nui-storage");
        foreach (var directory in new[] { serverCache, privateCache, gameStorage, nuiStorage })
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "data.bin"), directory);
        }

        var action = new LegacyServerCacheRepairAction(
            app,
            installation,
            CacheRepairPolicy.RepairNow,
            thresholdBytes: 1,
            new FakeProcessInspector());
        var context = Context();
        var result = await action.ApplyAsync(context, CancellationToken.None);
        await action.CommitAsync(context, result.SnapshotJson, CancellationToken.None);

        Assert.True(result.Changed);
        Assert.False(File.Exists(Path.Combine(serverCache, "data.bin")));
        Assert.False(File.Exists(Path.Combine(privateCache, "data.bin")));
        Assert.True(File.Exists(Path.Combine(gameStorage, "data.bin")));
        Assert.True(File.Exists(Path.Combine(nuiStorage, "data.bin")));
    }

    [Fact]
    public async Task OversizedPolicy_DoesNothingBelowThreshold()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var installation = temporaryDirectory.Combine("FiveM");
        var app = Path.Combine(installation, "FiveM.app");
        var serverCache = Path.Combine(app, "data", "server-cache");
        Directory.CreateDirectory(serverCache);
        var cacheFile = Path.Combine(serverCache, "small.bin");
        File.WriteAllText(cacheFile, "small");
        var action = new LegacyServerCacheRepairAction(
            app,
            installation,
            CacheRepairPolicy.WhenOversized,
            thresholdBytes: 1024,
            new FakeProcessInspector());

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.False(result.Changed);
        Assert.True(File.Exists(cacheFile));
    }

    private static WindowsActionContext Context()
    {
        return new WindowsActionContext
        {
            TransactionId = Guid.NewGuid(),
            StartedAtUtc = DateTimeOffset.UtcNow,
            IsElevated = false
        };
    }
}
