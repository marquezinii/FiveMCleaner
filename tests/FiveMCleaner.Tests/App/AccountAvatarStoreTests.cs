using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FiveMCleaner.App.Services;
using Xunit;

namespace FiveMCleaner.Tests.App;

/// <summary>
/// WPF's imaging pipeline (<see cref="BitmapImage"/>, WIC underneath it) is
/// exercised here, not just plain file IO, so every test runs on a
/// dedicated STA thread -- the same requirement <c>AccountWindowTests</c>
/// has for building a real window, and for the same underlying reason
/// (COM apartment affinity).
/// </summary>
public sealed class AccountAvatarStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "FiveMCleanerAvatarTests_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; a leftover temp directory does not fail the test.
        }
    }

    [Fact]
    public void TrySave_LargeRectangularImage_CropsToSquareAndCapsAtMaxDimension() => RunOnSta(() =>
    {
        var store = new AccountAvatarStore(directory);
        var source = CreateTestImage(1000, 700);

        Assert.True(store.TrySave("uid-1", source));

        var loaded = store.TryLoad("uid-1");
        Assert.NotNull(loaded);
        Assert.Equal(AccountAvatarStore.MaxDimension, loaded!.PixelWidth);
        Assert.Equal(AccountAvatarStore.MaxDimension, loaded.PixelHeight);
    });

    [Fact]
    public void TrySave_ImageSmallerThanMax_IsNeverUpscaled() => RunOnSta(() =>
    {
        var store = new AccountAvatarStore(directory);
        var source = CreateTestImage(50, 50);

        Assert.True(store.TrySave("uid-2", source));

        var loaded = store.TryLoad("uid-2");
        Assert.NotNull(loaded);
        Assert.Equal(50, loaded!.PixelWidth);
        Assert.Equal(50, loaded.PixelHeight);
    });

    [Fact]
    public void TryLoad_NothingSavedForThatUid_ReturnsNull() => RunOnSta(() =>
    {
        var store = new AccountAvatarStore(directory);
        Assert.Null(store.TryLoad("uid-without-a-photo"));
    });

    [Fact]
    public void TrySave_SourceFileDoesNotExist_ReturnsFalseAndSavesNothing() => RunOnSta(() =>
    {
        var store = new AccountAvatarStore(directory);
        Assert.False(store.TrySave("uid-3", Path.Combine(directory, "missing.png")));
        Assert.Null(store.TryLoad("uid-3"));
    });

    [Fact]
    public void TrySave_NotAnImage_ReturnsFalseWithoutThrowing() => RunOnSta(() =>
    {
        var store = new AccountAvatarStore(directory);
        Directory.CreateDirectory(directory);
        var notAnImage = Path.Combine(directory, "not-an-image.png");
        File.WriteAllText(notAnImage, "this is not a picture");

        Assert.False(store.TrySave("uid-4", notAnImage));
    });

    [Fact]
    public void TrySave_OverwritesAPreviousAvatarForTheSameUid() => RunOnSta(() =>
    {
        var store = new AccountAvatarStore(directory);
        Assert.True(store.TrySave("uid-5", CreateTestImage(200, 200)));
        Assert.True(store.TrySave("uid-5", CreateTestImage(300, 300)));

        var loaded = store.TryLoad("uid-5");
        Assert.NotNull(loaded);
        Assert.Equal(300, loaded!.PixelWidth);
    });

    [Fact]
    public void Delete_RemovesTheStoredAvatar() => RunOnSta(() =>
    {
        var store = new AccountAvatarStore(directory);
        store.TrySave("uid-6", CreateTestImage(100, 100));
        Assert.NotNull(store.TryLoad("uid-6"));

        store.Delete("uid-6");

        Assert.Null(store.TryLoad("uid-6"));
    });

    [Fact]
    public void Delete_NothingStored_DoesNotThrow() => RunOnSta(() =>
    {
        var store = new AccountAvatarStore(directory);
        store.Delete("uid-never-saved");
    });

    [Fact]
    public void PathFor_SanitizesTheUidForFilesystemSafety() => RunOnSta(() =>
    {
        var store = new AccountAvatarStore(directory);
        var path = store.PathFor("../../evil:uid?.png");
        Assert.DoesNotContain("..", path);
        Assert.DoesNotContain(":", Path.GetFileName(path));
        Assert.DoesNotContain("?", path);
    });

    /// <summary>Renders a flat-colored square-or-rectangular PNG at an exact pixel size, for use as a TrySave source.</summary>
    private string CreateTestImage(int width, int height)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"source-{width}x{height}-{Guid.NewGuid():N}.png");

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.CornflowerBlue, null, new Rect(0, 0, width, height));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private static void RunOnSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "O teste de avatar não terminou a tempo.");
        if (failure is not null) throw failure;
    }
}
