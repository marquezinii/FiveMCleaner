using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

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
        PackageSizeBytes.ToString(CultureInfo.InvariantCulture)]));
}

public static class ReleaseTrustPolicy
{
    private static readonly Regex StableVersion = new(
        "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public static void Verify(SignedReleaseManifest manifest, ReadOnlySpan<byte> publicKey, string highestConfirmedVersion)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (publicKey.Length is < 80 or > 256
            || manifest.Channel != "stable"
            || !StableVersion.IsMatch(manifest.Version)
            || !StableVersion.IsMatch(manifest.MinimumAllowedVersion)
            || !StableVersion.IsMatch(highestConfirmedVersion)
            || !Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out var url)
            || url.Scheme != Uri.UriSchemeHttps
            || !url.IsDefaultPort
            || !string.IsNullOrEmpty(url.UserInfo)
            || !string.IsNullOrEmpty(url.Query)
            || !string.IsNullOrEmpty(url.Fragment)
            || !url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !url.AbsolutePath.StartsWith("/marquezinii/FiveMCleaner/releases/download/", StringComparison.Ordinal)
            || manifest.PackageSizeBytes is <= 0 or > 1_073_741_824
            || manifest.PackageSha256.Length != 64 || !manifest.PackageSha256.All(char.IsAsciiHexDigit)
            || manifest.SignatureBase64.Length is < 80 or > 256)
            throw new InvalidDataException("Manifesto de release inválido.");

        if (!Version.TryParse(manifest.Version, out var candidate)
            || !Version.TryParse(manifest.MinimumAllowedVersion, out var minimum)
            || !Version.TryParse(highestConfirmedVersion, out var highest)
            || highest < minimum || candidate < minimum || candidate < highest)
            throw new InvalidDataException("A política anti-downgrade rejeitou a release.");

        SignatureVerification.Verify(
            manifest.CanonicalPayload(), publicKey, manifest.SignatureBase64,
            "Assinatura de release inválida.",
            "Chave pública do manifesto é inválida.",
            "Assinatura do manifesto não confere.");
    }
}
