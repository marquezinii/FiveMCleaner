using System.Globalization;
using System.Text;

namespace Ralven.UpdateRuntime;

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
    public const string ExpectedProduct = "Ralven";
    public const string ExpectedFileManifestRelativePath = "broker/SHA256SUMS.txt";
    public const int MaximumSignedManifestBytes = 16 * 1024;
    public const int MaximumFileManifestBytes = 64 * 1024;

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

        SignatureVerification.Verify(
            manifest.CanonicalPayload(), publicKey, manifest.SignatureBase64,
            "Assinatura do componente administrativo inválida.",
            "Chave pública do componente administrativo inválida.",
            "Assinatura do componente administrativo não confere.");
    }
}
