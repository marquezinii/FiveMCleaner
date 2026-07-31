using System.Security.Cryptography;
using System.Text;

namespace FiveMCleaner.UpdateRuntime;

/// <summary>Minimal signed release contract. Transport and storage are deliberately separate.</summary>
public sealed record SignedReleaseManifest(
    string Channel,
    string Version,
    string MinimumAllowedVersion,
    string PackageUrl,
    string PackageSha256,
    long PackageSizeBytes,
    string SignatureBase64)
{
    public byte[] CanonicalPayload() => Encoding.UTF8.GetBytes(string.Join("\n", [
        Channel, Version, MinimumAllowedVersion, PackageUrl, PackageSha256.ToLowerInvariant(),
        PackageSizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)]));
}

public static class ReleaseTrustPolicy
{
    public static void Verify(SignedReleaseManifest manifest, ReadOnlySpan<byte> publicKey, string highestConfirmedVersion)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (publicKey.Length != 32 || !Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out var url)
            || url.Scheme != Uri.UriSchemeHttps || manifest.PackageSizeBytes <= 0
            || manifest.PackageSha256.Length != 64 || !manifest.PackageSha256.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("Manifesto de release inválido.");

        if (!Version.TryParse(manifest.Version, out var candidate)
            || !Version.TryParse(manifest.MinimumAllowedVersion, out var minimum)
            || !Version.TryParse(highestConfirmedVersion, out var highest)
            || candidate < minimum || candidate < highest)
            throw new InvalidDataException("A política anti-downgrade rejeitou a release.");

        byte[] signature;
        try { signature = Convert.FromBase64String(manifest.SignatureBase64); }
        catch (FormatException) { throw new InvalidDataException("Assinatura de release inválida."); }
        if (signature.Length != 64 || !Ed25519.Verify(signature, manifest.CanonicalPayload(), publicKey))
            throw new CryptographicException("Assinatura do manifesto não confere.");
    }
}
