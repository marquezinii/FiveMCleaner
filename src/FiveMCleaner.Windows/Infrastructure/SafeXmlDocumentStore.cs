using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FiveMCleaner.Windows.Infrastructure;

/// <summary>
/// Shared safe XML load/save/hash/atomic-replace mechanics for the
/// FiveM/GTA V settings actions (<c>gta5_settings.xml</c>/<c>settings.xml</c>),
/// extracted from <c>LegacyGraphicsPresetAction</c> and
/// <c>DisplayPreferencesAction</c>, which previously duplicated it byte for
/// byte.
/// </summary>
internal static class SafeXmlDocumentStore
{
    private const int MaxDocumentBytes = 4 * 1024 * 1024;

    public static XDocument LoadSafeDocument(string path, string sizeExceededMessage)
    {
        return LoadSafeDocumentWithHash(path, sizeExceededMessage).Document;
    }

    public static (XDocument Document, string Sha256) LoadSafeDocumentWithHash(
        string path,
        string sizeExceededMessage)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = false,
            MaxCharactersInDocument = MaxDocumentBytes
        };
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxDocumentBytes)
        {
            throw new InvalidDataException(sizeExceededMessage);
        }

        using var buffer = new MemoryStream((int)stream.Length);
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        buffer.Position = 0;
        using var reader = XmlReader.Create(buffer, settings);
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        return (document, hash);
    }

    public static void SaveDocument(XDocument document, string path)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            NewLineHandling = NewLineHandling.None,
            CloseOutput = true
        };
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = XmlWriter.Create(stream, settings);
        document.Save(writer);
    }

    public static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static void ReplaceAndVerifyDisplacedOriginal(
        string replacementPath,
        string destinationPath,
        string displacedPath,
        string expectedDisplacedSha256,
        string conflictMessage)
    {
        File.Replace(replacementPath, destinationPath, displacedPath, ignoreMetadataErrors: true);
        Exception? validationError = null;
        var matches = false;
        try
        {
            matches = ComputeSha256(displacedPath).Equals(
                expectedDisplacedSha256,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            validationError = exception;
        }

        if (matches)
        {
            return;
        }

        try
        {
            File.Replace(displacedPath, destinationPath, null, ignoreMetadataErrors: true);
        }
        catch (Exception restoreException) when (restoreException is IOException
            or UnauthorizedAccessException)
        {
            throw new IOException(
                $"Não foi possível confirmar a troca; a versão deslocada ficou preservada em '{displacedPath}'.",
                new AggregateException(
                    validationError ?? new IOException("Hash deslocado divergente."),
                    restoreException));
        }

        throw new IOException(conflictMessage, validationError);
    }
}
