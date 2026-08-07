using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using FiveMCleaner.App.Services;
using Xunit;

namespace FiveMCleaner.Tests.App;

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
            "FiveMCleaner-Runtime-9.9.9-win-x64.zip",
            new Uri("https://github.com/marquezinii/FiveMCleaner/releases/download/v9.9.9/package.zip"),
            payload.LongLength,
            sha256,
            new Uri("https://example.invalid/notes"));

        var downloaded = await service.DownloadUpdateAsync(update);

        Assert.False(downloaded.WasAlreadyDownloaded);
        Assert.True(File.Exists(downloaded.InstallerPath));
        Assert.Equal(payload, await File.ReadAllBytesAsync(downloaded.InstallerPath));
        Assert.Empty(Directory.GetFiles(
            Path.GetDirectoryName(downloaded.InstallerPath)!,
            "*.part"));
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
