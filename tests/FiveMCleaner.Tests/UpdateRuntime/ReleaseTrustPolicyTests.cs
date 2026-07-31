using System.Security.Cryptography;
using FiveMCleaner.UpdateRuntime;
using Xunit;

namespace FiveMCleaner.Tests.UpdateRuntime;

public sealed class ReleaseTrustPolicyTests
{
    [Fact]
    public void Verify_AcceptsSignedForwardRelease_AndRejectsDowngrade()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = signer.ExportSubjectPublicKeyInfo();
        var unsigned = new SignedReleaseManifest("stable", "2.0.0", "1.5.0", "https://github.com/marquezinii/FiveMCleaner/releases/download/v2.0.0/app.zip", new string('a', 64), 1024, "");
        var signature = signer.SignData(unsigned.CanonicalPayload(), HashAlgorithmName.SHA256);
        var valid = unsigned with { SignatureBase64 = Convert.ToBase64String(signature) };

        ReleaseTrustPolicy.Verify(valid, publicKey, "1.9.0");
        Assert.Throws<InvalidDataException>(() => ReleaseTrustPolicy.Verify(valid with { Version = "1.0.0" }, publicKey, "1.9.0"));
        Assert.Throws<InvalidDataException>(() => ReleaseTrustPolicy.Verify(valid with { Version = "2.0" }, publicKey, "1.9.0"));
        Assert.Throws<InvalidDataException>(() => ReleaseTrustPolicy.Verify(
            valid with { PackageUrl = "https://example.com/app.zip" }, publicKey, "1.9.0"));
        Assert.Throws<InvalidDataException>(() => ReleaseTrustPolicy.Verify(
            valid with { MinimumAllowedVersion = "2.0.0" }, publicKey, "1.9.0"));
    }
}
