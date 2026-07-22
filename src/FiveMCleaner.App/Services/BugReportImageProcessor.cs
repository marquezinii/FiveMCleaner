using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FiveMCleaner.App.Services;

public static class BugReportImageProcessor
{
    public const int MaximumAttachmentBytes = 8 * 1024 * 1024;
    private const long MaximumPixels = 32_000_000;

    public static BugReportAttachment LoadSanitizedImage(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var extension = Path.GetExtension(fullPath);
        if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Selecione uma imagem PNG ou JPEG.");
        }

        var file = new FileInfo(fullPath);
        if (!file.Exists || file.Length is <= 0 or > MaximumAttachmentBytes)
        {
            throw new InvalidDataException("A imagem precisa existir e ter no máximo 8 MB.");
        }

        using var source = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        ValidateSignature(source, extension);
        source.Position = 0;
        var decoder = BitmapDecoder.Create(
            source,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.FirstOrDefault()
            ?? throw new InvalidDataException("A imagem não possui um quadro válido.");
        var pixels = checked((long)frame.PixelWidth * frame.PixelHeight);
        if (pixels <= 0 || pixels > MaximumPixels)
        {
            throw new InvalidDataException("A resolução da imagem é grande demais.");
        }

        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        var sanitizedFrame = BitmapFrame.Create(converted);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(sanitizedFrame);
        using var destination = new MemoryStream();
        encoder.Save(destination);
        if (destination.Length > MaximumAttachmentBytes)
        {
            throw new InvalidDataException("A imagem sanitizada ultrapassa 8 MB.");
        }

        return new BugReportAttachment(
            $"captura-{Guid.NewGuid():N}.png",
            "image/png",
            destination.ToArray());
    }

    private static void ValidateSignature(Stream stream, string extension)
    {
        Span<byte> signature = stackalloc byte[8];
        var read = stream.Read(signature);
        var isPng = read == 8
            && signature.SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var isJpeg = read >= 3
            && signature[0] == 0xFF
            && signature[1] == 0xD8
            && signature[2] == 0xFF;
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) && !isPng
            || (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)) && !isJpeg)
        {
            throw new InvalidDataException("A extensão não corresponde ao conteúdo real da imagem.");
        }
    }
}
