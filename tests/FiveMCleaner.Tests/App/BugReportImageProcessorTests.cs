using System.Windows.Media;
using System.Windows.Media.Imaging;
using FiveMCleaner.App.Services;
using FiveMCleaner.Tests.Windows;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class BugReportImageProcessorTests
{
    [Fact]
    public void LoadSanitizedImage_ReencodesPngWithoutOriginalMetadataOrName()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var sourcePath = temporaryDirectory.Combine("nome-pessoal.png");
        var pixels = new byte[]
        {
            0, 122, 255, 255, 0, 122, 255, 255,
            0, 122, 255, 255, 0, 122, 255, 255
        };
        var bitmap = BitmapSource.Create(
            2,
            2,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            8);
        var metadata = new BitmapMetadata("png")
        {
            ApplicationName = "Aplicativo pessoal",
            Comment = "dado privado"
        };
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        using (var stream = File.Create(sourcePath))
        {
            encoder.Save(stream);
        }

        var attachment = BugReportImageProcessor.LoadSanitizedImage(sourcePath);

        Assert.StartsWith("captura-", attachment.FileName, StringComparison.Ordinal);
        Assert.EndsWith(".png", attachment.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nome-pessoal", attachment.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("image/png", attachment.ContentType);
        using var sanitized = new MemoryStream(attachment.Content);
        var decoded = BitmapDecoder.Create(
            sanitized,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var sanitizedMetadata = decoded.Frames[0].Metadata as BitmapMetadata;
        Assert.True(string.IsNullOrWhiteSpace(sanitizedMetadata?.ApplicationName));
        Assert.True(string.IsNullOrWhiteSpace(sanitizedMetadata?.Comment));
    }

    [Fact]
    public void LoadSanitizedImage_RejectsExtensionThatDoesNotMatchContent()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = temporaryDirectory.Combine("falso.png");
        File.WriteAllText(path, "isto não é uma imagem");

        Assert.Throws<InvalidDataException>(() =>
            BugReportImageProcessor.LoadSanitizedImage(path));
    }
}
