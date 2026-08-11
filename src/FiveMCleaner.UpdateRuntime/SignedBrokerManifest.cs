using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FiveMCleaner.UpdateRuntime;

/// <summary>
/// Signed identity for the only executable that the desktop process starts
/// through UAC. The manifest travels beside the versioned runtime, while its
/// trust anchor remains embedded in the application.
/// </summary>
public sealed record SignedBrokerManifest(
    int SchemaVersion,
    string Product,
    string Version,
    string FileManifestRelativePath,
    string FileManifestSha256,
    string SignatureBase64)
{
    public byte[] CanonicalPayload() => Encoding.UTF8.GetBytes(string.Join("\n", [
        SchemaVersion.ToString(CultureInfo.InvariantCulture),
        Product,
        Version,
        FileManifestRelativePath,
        FileManifestSha256.ToLowerInvariant()
    ]));
}

public static class BrokerTrustPolicy
{
    public const string ManifestFileName = "broker-integrity.json";
    public const string ExpectedProduct = "FiveMCleaner";
    public const string ExpectedFileManifestRelativePath = "broker/SHA256SUMS.txt";

    public static void VerifySignature(
        SignedBrokerManifest manifest,
        ReadOnlySpan<byte> publicKey,
        string expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (publicKey.Length is < 80 or > 256
            || manifest.SchemaVersion != 1
            || manifest.Product != ExpectedProduct
            || manifest.Version != expectedVersion
            || manifest.FileManifestRelativePath != ExpectedFileManifestRelativePath
            || manifest.FileManifestSha256.Length != 64
            || !manifest.FileManifestSha256.All(char.IsAsciiHexDigit)
            || manifest.SignatureBase64.Length is < 80 or > 256)
        {
            throw new InvalidDataException("Manifesto de integridade do componente administrativo inválido.");
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(manifest.SignatureBase64);
        }
        catch (FormatException)
        {
            throw new InvalidDataException("Assinatura do componente administrativo inválida.");
        }

        using var verifier = ECDsa.Create();
        try
        {
            verifier.ImportSubjectPublicKeyInfo(publicKey, out var bytesRead);
            if (bytesRead != publicKey.Length)
            {
                throw new CryptographicException("Chave pública contém dados extras.");
            }
        }
        catch (CryptographicException)
        {
            throw new CryptographicException("Chave pública do componente administrativo inválida.");
        }

        if (!verifier.VerifyData(manifest.CanonicalPayload(), signature, HashAlgorithmName.SHA256))
        {
            throw new CryptographicException("Assinatura do componente administrativo não confere.");
        }
    }
}
