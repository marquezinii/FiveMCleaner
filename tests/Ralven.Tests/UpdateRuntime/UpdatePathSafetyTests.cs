using Ralven.UpdateRuntime;
using Xunit;

namespace Ralven.Tests.UpdateRuntime;

public sealed class UpdatePathSafetyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "RalvenUpdatePathSafety", Guid.NewGuid().ToString("N"));

    [Fact]
    public void EnsureNoReparsePoints_RejectsAnExistingLinkedAncestor()
    {
        var target = Path.Combine(root, "target");
        var link = Path.Combine(root, "linked");
        Directory.CreateDirectory(target);
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException or PlatformNotSupportedException)
        {
            return;
        }

        Assert.Throws<IOException>(() => UpdatePathSafety.EnsureNoReparsePoints(
            Path.Combine(link, "nested", "update.zip")));
    }

    [Fact]
    public void UpdateRuntimeStateStores_RejectALinkedRoot()
    {
        var target = Path.Combine(root, "target");
        var link = Path.Combine(root, "linked");
        Directory.CreateDirectory(target);
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException or PlatformNotSupportedException)
        {
            return;
        }

        Assert.Throws<IOException>(() => new RuntimeActivationStore(link));
        Assert.Throws<IOException>(() => new UpdateRecoveryJournal(link));
        Assert.Throws<IOException>(() => new UpdateHealthReceiptStore(link));
        Assert.Throws<IOException>(() => new VersionFloorStore(link));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
