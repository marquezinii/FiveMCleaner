using System.Security.Cryptography;
using System.Text.Json;
using Ralven.App.Services;
using Ralven.UpdateRuntime;
using Xunit;

namespace Ralven.Tests.App;

public sealed class BrokerIntegrityVerifierTests : IDisposable
{
    private readonly string temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "Ralven.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Verify_AcceptsSignedBrokerTreeAndLocksItUntilElevationStarts()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateSignedFixture(key);

        using var lease = BrokerIntegrityVerifier.Verify(
            fixture.VersionRoot,
            fixture.BrokerPath,
            key.ExportSubjectPublicKeyInfo(),
            requireSignedManifest: true);

        Assert.Throws<IOException>(() =>
            new FileStream(fixture.BrokerPath, FileMode.Open, FileAccess.Write, FileShare.None));
    }

    [Fact]
    public void Verify_AcceptsRealisticFileManifestLargerThanSixteenKibibytes()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateSignedFixture(key, dependencyCount: 202);
        var manifestPath = Path.Combine(fixture.VersionRoot, "broker", "SHA256SUMS.txt");

        Assert.InRange(
            new FileInfo(manifestPath).Length,
            16 * 1024 + 1,
            BrokerTrustPolicy.MaximumFileManifestBytes);
        using var lease = BrokerIntegrityVerifier.Verify(
            fixture.VersionRoot,
            fixture.BrokerPath,
            key.ExportSubjectPublicKeyInfo(),
            requireSignedManifest: true);
    }

    [Fact]
    public void Verify_RejectsFileManifestLargerThanPolicyLimit()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateSignedFixture(
            key,
            manifestPadding: new string(' ', BrokerTrustPolicy.MaximumFileManifestBytes));

        Assert.Throws<InvalidDataException>(() => BrokerIntegrityVerifier.Verify(
            fixture.VersionRoot,
            fixture.BrokerPath,
            key.ExportSubjectPublicKeyInfo(),
            requireSignedManifest: true));
    }

    [Fact]
    public void Verify_RejectsBrokerFileChangedAfterSigning()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateSignedFixture(key);
        File.AppendAllText(fixture.BrokerPath, "tampered");

        Assert.Throws<CryptographicException>(() => BrokerIntegrityVerifier.Verify(
            fixture.VersionRoot,
            fixture.BrokerPath,
            key.ExportSubjectPublicKeyInfo(),
            requireSignedManifest: true));
    }

    [Fact]
    public void Verify_RejectsUnsignedInstalledRuntimeButAllowsLocalBuildLayout()
    {
        var versionRoot = Path.Combine(temporaryRoot, "Runtime", "versions", "1.3.2");
        var brokerDirectory = Path.Combine(versionRoot, "broker");
        Directory.CreateDirectory(brokerDirectory);
        var brokerPath = Path.Combine(brokerDirectory, "Ralven.Broker.exe");
        File.WriteAllText(brokerPath, "development-broker");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = key.ExportSubjectPublicKeyInfo();

        Assert.Throws<InvalidDataException>(() => BrokerIntegrityVerifier.Verify(
            versionRoot,
            brokerPath,
            publicKey,
            requireSignedManifest: true));

        using var lease = BrokerIntegrityVerifier.Verify(
            versionRoot,
            brokerPath,
            publicKey,
            requireSignedManifest: false);
    }

    [Fact]
    public void IsVersionedRuntimeLayout_DistinguishesInstalledAndDevelopmentPaths()
    {
        Assert.True(BrokerIntegrityVerifier.IsVersionedRuntimeLayout(
            Path.Combine(temporaryRoot, "Runtime", "versions", "1.3.2")));
        Assert.False(BrokerIntegrityVerifier.IsVersionedRuntimeLayout(
            Path.Combine(temporaryRoot, "src", "Ralven.App", "bin", "Release", "net10.0-windows")));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryRoot))
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private BrokerFixture CreateSignedFixture(
        ECDsa key,
        int dependencyCount = 1,
        string manifestPadding = "")
    {
        var versionRoot = Path.Combine(temporaryRoot, "Runtime", "versions", "1.3.2");
        var brokerDirectory = Path.Combine(versionRoot, "broker");
        Directory.CreateDirectory(brokerDirectory);
        var brokerPath = Path.Combine(brokerDirectory, "Ralven.Broker.exe");
        File.WriteAllText(brokerPath, "broker-binary");
        var checksumLines = new List<string> { ChecksumLine(brokerPath, "Ralven.Broker.exe") };
        for (var index = 0; index < dependencyCount; index++)
        {
            var name = $"Ralven.Dependency.{index:D3}.dll";
            var dependencyPath = Path.Combine(brokerDirectory, name);
            File.WriteAllText(dependencyPath, $"dependency-{index}");
            checksumLines.Add(ChecksumLine(dependencyPath, name));
        }

        var fileManifestPath = Path.Combine(brokerDirectory, "SHA256SUMS.txt");
        File.WriteAllLines(fileManifestPath, checksumLines);
        File.AppendAllText(fileManifestPath, manifestPadding);
        var fileManifestHash = Convert.ToHexString(
            SHA256.HashData(File.ReadAllBytes(fileManifestPath))).ToLowerInvariant();
        var unsigned = new SignedBrokerManifest(
            1,
            BrokerTrustPolicy.ExpectedProduct,
            "1.3.2",
            BrokerTrustPolicy.ExpectedFileManifestRelativePath,
            fileManifestHash,
            string.Empty);
        var signed = unsigned with
        {
            SignatureBase64 = Convert.ToBase64String(
                key.SignData(unsigned.CanonicalPayload(), HashAlgorithmName.SHA256))
        };
        File.WriteAllText(
            Path.Combine(versionRoot, BrokerTrustPolicy.ManifestFileName),
            JsonSerializer.Serialize(signed));
        return new BrokerFixture(versionRoot, brokerPath);
    }

    private static string ChecksumLine(string path, string relative) =>
        $"{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()}  {relative}";

    private sealed record BrokerFixture(string VersionRoot, string BrokerPath);
}
