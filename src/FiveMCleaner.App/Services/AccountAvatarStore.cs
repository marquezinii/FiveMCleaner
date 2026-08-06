using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using FiveMCleaner.Contracts;

namespace FiveMCleaner.App.Services;

/// <summary>
/// Stores the signed-in user's profile photo locally, keyed by Firebase UID.
///
/// There is no server-side avatar storage: the Worker's bug-report attachment
/// feature already hit this exact wall and was dropped after R2 turned out to
/// require an account-level activation step in the Cloudflare Dashboard that
/// could not be completed in this environment (see
/// <c>infra/cloudflare-worker/README.md</c>). The same blocker applies here,
/// so a photo set on one machine does not follow the account to another
/// install until that infrastructure exists.
///
/// Every save normalizes the source image to a square PNG capped at
/// <see cref="MaxDimension"/> px, regardless of what the user picked, so
/// on-disk size (and the eventual network cost, once sync exists) stays
/// bounded.
/// </summary>
public sealed class AccountAvatarStore
{
    public const int MaxDimension = 512;
    public const long MaxSourceFileBytes = 8 * 1024 * 1024;

    private readonly string directory;

    public AccountAvatarStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductIdentity.Name,
            "avatars"))
    {
    }

    internal AccountAvatarStore(string directory)
    {
        this.directory = directory;
    }

    /// <summary>Path the avatar for <paramref name="uid"/> is stored at, whether or not it exists yet.</summary>
    public string PathFor(string uid) => Path.Combine(directory, $"{Sanitize(uid)}.png");

    /// <summary>Loads the stored avatar for <paramref name="uid"/>, if any. Never throws.</summary>
    public BitmapImage? TryLoad(string uid)
    {
        var path = PathFor(uid);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the image at <paramref name="sourcePath"/>, center-crops it to a
    /// square, downsizes it to <see cref="MaxDimension"/> px if larger, and
    /// saves it as <paramref name="uid"/>'s avatar. Returns false — without
    /// writing anything — for a file that does not exist, is not a readable
    /// image, or exceeds <see cref="MaxSourceFileBytes"/>.
    /// </summary>
    public bool TrySave(string uid, string sourcePath)
    {
        try
        {
            var info = new FileInfo(sourcePath);
            if (!info.Exists || info.Length > MaxSourceFileBytes)
            {
                return false;
            }

            var source = new BitmapImage();
            source.BeginInit();
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.UriSource = new Uri(info.FullName, UriKind.Absolute);
            source.EndInit();

            var side = Math.Min(source.PixelWidth, source.PixelHeight);
            if (side <= 0)
            {
                return false;
            }

            var cropped = new CroppedBitmap(source, new Int32Rect(
                (source.PixelWidth - side) / 2,
                (source.PixelHeight - side) / 2,
                side,
                side));

            var scale = (double)MaxDimension / side;
            BitmapSource square = scale < 1
                ? new TransformedBitmap(cropped, new System.Windows.Media.ScaleTransform(scale, scale))
                : cropped;

            Directory.CreateDirectory(directory);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(square));

            // Write-then-move so a save that fails partway (disk full, killed
            // process) can never leave a half-written avatar in place of a
            // previously good one.
            var destination = PathFor(uid);
            var temporary = destination + ".tmp";
            using (var stream = File.Create(temporary))
            {
                encoder.Save(stream);
            }
            File.Move(temporary, destination, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException or NotImplementedException)
        {
            return false;
        }
    }

    /// <summary>Removes the stored avatar for <paramref name="uid"/>, if any. Never throws.</summary>
    public void Delete(string uid)
    {
        try
        {
            File.Delete(PathFor(uid));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    // Firebase UIDs are opaque, URL-safe identifiers from a verified ID
    // token, not user input -- but keying a filesystem path on any external
    // string deserves the same defensive normalization regardless.
    private static string Sanitize(string uid) =>
        string.Concat(uid.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_'));
}
