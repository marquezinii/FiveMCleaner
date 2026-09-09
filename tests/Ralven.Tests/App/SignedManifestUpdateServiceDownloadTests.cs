using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Ralven.App.Services;
using Xunit;

namespace Ralven.Tests.App;

public sealed class SignedManifestUpdateServiceDownloadTests
{
    [Fact]
    public async Task DownloadUpdateAsync_MovesTheVerifiedPackageIntoPlace()
    {
        var payload = new byte[64 * 1024 + 7];
        Random.Shared.NextBytes(payload);
        var sha256 = Convert.ToHexString(SHA256.HashData(payload));

        using var temporaryData = new TemporaryDirectory();
        using var handler = new PayloadHandler(payload);
        using var service = new SignedManifestUpdateService(handler, temporaryData.Path);

        var update = new ReleaseUpdate(
            StableSemanticVersion.Parse("9.9.9"),
            "v9.9.9",
            "Ralven-Runtime-9.9.9-win-x64.zip",
            new Uri("https://vemryx.com/Ralven/releases/v9.9.9/Ralven-Runtime-9.9.9-win-x64.zip"),
            payload.LongLength,
            sha256,
            new Uri("https://example.invalid/notes"));

        var downloaded = await service.DownloadUpdateAsync(update, cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.False(downloaded.WasAlreadyDownloaded);
        Assert.True(File.Exists(downloaded.InstallerPath));
        Assert.Equal(payload, await File.ReadAllBytesAsync(downloaded.InstallerPath, cancellationToken: global::Xunit.TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(
            Path.GetDirectoryName(downloaded.InstallerPath)!,
            "*.part"));
    }

    [Fact]
    public async Task DownloadUpdateAsync_PrunesOnlyStaleVersionCaches()
    {
        var payload = "runtime"u8.ToArray();
        var sha256 = Convert.ToHexString(SHA256.HashData(payload));
        using var temporaryData = new TemporaryDirectory();
        var updatesRoot = Path.Combine(temporaryData.Path, "Updates");
        Directory.CreateDirectory(Path.Combine(updatesRoot, "1.0.0"));
        Directory.CreateDirectory(Path.Combine(updatesRoot, "manual-recovery"));
        File.WriteAllText(Path.Combine(updatesRoot, "1.0.0", "old.zip"), "old");
        using var handler = new PayloadHandler(payload);
        using var service = new SignedManifestUpdateService(handler, temporaryData.Path);
        var update = new ReleaseUpdate(
            StableSemanticVersion.Parse("2.0.0"),
            "v2.0.0",
            "Ralven-Runtime-2.0.0-win-x64.zip",
            new Uri("https://vemryx.com/Ralven/releases/v2.0.0/Ralven-Runtime-2.0.0-win-x64.zip"),
            payload.LongLength,
            sha256,
            new Uri("https://example.invalid/notes"));

        await service.DownloadUpdateAsync(update, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(Directory.Exists(Path.Combine(updatesRoot, "1.0.0")));
        Assert.True(Directory.Exists(Path.Combine(updatesRoot, "manual-recovery")));
        Assert.True(Directory.Exists(Path.Combine(updatesRoot, "2.0.0")));
    }

    [Fact]
    public async Task DownloadUpdateAsync_RejectsAPathOutsideTheSignedPackageRoute()
    {
        var payload = "installer"u8.ToArray();
        using var temporaryData = new TemporaryDirectory();
        using var handler = new PayloadHandler(payload);
        using var service = new SignedManifestUpdateService(
            handler,
            temporaryData.Path,
            ReleasePackageKind.Installer);
        var update = new ReleaseUpdate(
            StableSemanticVersion.Parse("2.0.0"),
            "v2.0.0",
            "Ralven-Setup-2.0.0-win-x64.exe",
            new Uri("https://vemryx.com/Ralven/download/"),
            payload.LongLength,
            Convert.ToHexString(SHA256.HashData(payload)));

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            service.DownloadUpdateAsync(update, cancellationToken: TestContext.Current.CancellationToken));

        var diagnostic = await File.ReadAllTextAsync(
            Path.Combine(temporaryData.Path, "Logs", "updater.jsonl"),
            TestContext.Current.CancellationToken);
        Assert.Contains("\"ErrorCode\":\"u201\"", diagnostic, StringComparison.Ordinal);
    }

    private sealed class PayloadHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            });
    }
}
