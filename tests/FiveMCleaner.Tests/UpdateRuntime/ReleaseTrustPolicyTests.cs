using System.Security.Cryptography;
using FiveMCleaner.UpdateRuntime;
using Xunit;

namespace FiveMCleaner.Tests.UpdateRuntime;

public sealed class ReleaseTrustPolicyTests
{
    [Fact]
    public void Verify_AcceptsSignedForwardRelease_AndRejectsDowngrade()
    {
        var privateKey = new byte[32];
        RandomNumberGenerator.Fill(privateKey);
        var publicKey = new byte[32];
        Ed25519.GeneratePublicKey(privateKey, publicKey);
        var unsigned = new SignedReleaseManifest("stable", "2.0.0", "1.5.0", "https://github.com/marquezinii/FiveMCleaner/releases/download/v2.0.0/app.zip", new string('a', 64), 1024, "");
        var signature = new byte[64];
        Ed25519.Sign(unsigned.CanonicalPayload(), privateKey, signature);
        var valid = unsigned with { SignatureBase64 = Convert.ToBase64String(signature) };

        ReleaseTrustPolicy.Verify(valid, publicKey, "1.9.0");
        Assert.Throws<InvalidDataException>(() => ReleaseTrustPolicy.Verify(valid with { Version = "1.0.0" }, publicKey, "1.9.0"));
    }
}
