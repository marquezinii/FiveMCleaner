using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FiveMCleaner.App.Services;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class GitHubReleaseUpdateServiceTests
{
    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("v1.2.3", true)]
    [InlineData("1.2.3+build.7", true)]
    [InlineData("1.2.3-beta.1", false)]
    [InlineData("01.2.3", false)]
    [InlineData("1.2", false)]
    [InlineData("1.2.3.4", false)]
    [InlineData(" V1.2.3 ", false)]
    public void StableSemanticVersion_OnlyAcceptsStableSemVer(string value, bool expected)
    {
        Assert.Equal(expected, StableSemanticVersion.TryParse(value, out _));
    }

    [Fact]
    public void StableSemanticVersion_ComparesNumericComponentsAndIgnoresBuildMetadata()
    {
        var older = StableSemanticVersion.Parse("999999999999999999999999.2.3+first");
        var same = StableSemanticVersion.Parse("999999999999999999999999.2.3+second");
        var newer = StableSemanticVersion.Parse("999999999999999999999999.2.4");

        Assert.Equal(0, older.CompareTo(same));
        Assert.True(newer.CompareTo(older) > 0);
    }

    [Fact]
    public async Task CheckForUpdate_UsesOfficialEndpointHeadersAndReturnsNewerRelease()
    {
        using var scope = new TemporaryDirectory();
        var installer = Encoding.UTF8.GetBytes("verified setup payload");
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Equal(
                "https://api.github.com/repos/marquezinii/FiveMCleaner/releases/latest",
                request.RequestUri?.AbsoluteUri);
            Assert.Contains("FiveMCleaner-Updater/1.0", request.Headers.UserAgent.ToString());
            Assert.Contains(
                request.Headers.Accept,
                value => value.MediaType == "application/vnd.github+json");
            Assert.Equal("2022-11-28", request.Headers.GetValues("X-GitHub-Api-Version").Single());
            return Task.FromResult(JsonResponse(request, CreateManifest("v1.2.3", installer)));
        });
        using var service = CreateService(handler, scope.Path);

        var update = await service.CheckForUpdateAsync("1.2.2");

        Assert.NotNull(update);
        Assert.Equal("1.2.3", update.Version.ToString());
        Assert.Equal("v1.2.3", update.TagName);
        Assert.Equal("FiveMCleaner-Setup-1.2.3-win-x64.exe", update.AssetName);
        Assert.Equal(installer.Length, update.SizeBytes);
        Assert.Equal(Sha256Hex(installer), update.Sha256Hex);
        Assert.Equal(OfficialReleaseNotesUrl("v1.2.3"), update.ReleaseNotesUri?.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://example.test/release")]
    [InlineData("https://github.com/marquezinii/FiveMCleaner/releases/tag/v2.0.1")]
    [InlineData("http://github.com/marquezinii/FiveMCleaner/releases/tag/v2.0.0")]
    public async Task CheckForUpdate_RejectsUntrustedReleaseNotesUrl(string releaseNotesUrl)
    {
        using var scope = new TemporaryDirectory();
        var manifest = CreateManifest(
            "v2.0.0",
            Encoding.UTF8.GetBytes("setup"),
            releaseNotesUrl: releaseNotesUrl);
        using var service = CreateService(ManifestOnlyHandler(manifest), scope.Path);

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            service.CheckForUpdateAsync("1.0.0"));
    }

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("2.0.0")]
    public async Task CheckForUpdate_ReturnsNullWhenLatestIsNotNewer(string currentVersion)
    {
        using var scope = new TemporaryDirectory();
        var installer = Encoding.UTF8.GetBytes("setup");
        var handler = ManifestOnlyHandler(CreateManifest("v1.2.3", installer));
        using var service = CreateService(handler, scope.Path);

        var update = await service.CheckForUpdateAsync(currentVersion);

        Assert.Null(update);
    }

    [Fact]
    public async Task CheckForUpdate_ReturnsNullWhenRepositoryHasNoPublishedRelease()
    {
        using var scope = new TemporaryDirectory();
        var handler = new StubHttpMessageHandler((request, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request }));
        using var service = CreateService(handler, scope.Path);

        Assert.Null(await service.CheckForUpdateAsync("1.0.0"));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CheckForUpdate_RejectsDraftAndPrerelease(bool draft, bool prerelease)
    {
        using var scope = new TemporaryDirectory();
        var manifest = CreateManifest(
            "v2.0.0",
            Encoding.UTF8.GetBytes("setup"),
            draft: draft,
            prerelease: prerelease);
        using var service = CreateService(ManifestOnlyHandler(manifest), scope.Path);

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            service.CheckForUpdateAsync("1.0.0"));
    }

    [Theory]
    [InlineData("v2.0.0-rc.1")]
    [InlineData("2.0.0")]
    [InlineData("v2.0.0+build.1")]
    [InlineData("latest")]
    [InlineData("02.0.0")]
    public async Task CheckForUpdate_RejectsNonStableReleaseTags(string tag)
    {
        using var scope = new TemporaryDirectory();
        using var service = CreateService(
            ManifestOnlyHandler(CreateManifest(tag, Encoding.UTF8.GetBytes("setup"))),
            scope.Path);

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            service.CheckForUpdateAsync("1.0.0"));
    }

    [Theory]
    [InlineData("draft", "false", "draft", "true")]
    [InlineData("tag_name", "\"v2.0.0\"", "tag_name", "\"v9.0.0\"")]
    public async Task CheckForUpdate_RejectsDuplicateSecurityCriticalProperties(
        string firstName,
        string firstValue,
        string duplicateName,
        string duplicateValue)
    {
        using var scope = new TemporaryDirectory();
        var bytes = Encoding.UTF8.GetBytes("setup");
        var asset = CreateAssetJson("v2.0.0", bytes);
        var json = $$"""
            {
              "{{firstName}}": {{firstValue}},
              "{{duplicateName}}": {{duplicateValue}},
              "prerelease": false,
              "tag_name": "v2.0.0",
              "assets": [{{asset}}]
            }
            """;
        using var service = CreateService(ManifestOnlyHandler(json), scope.Path);

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            service.CheckForUpdateAsync("1.0.0"));
    }

    [Fact]
    public async Task CheckForUpdate_RejectsDuplicateMatchingInstallerAssets()
    {
        using var scope = new TemporaryDirectory();
        var bytes = Encoding.UTF8.GetBytes("setup");
        var asset = CreateAssetJson("v2.0.0", bytes);
        var json = $$"""
            {
              "draft": false,
              "prerelease": false,
              "tag_name": "v2.0.0",
              "assets": [{{asset}}, {{asset}}]
            }
            """;
        using var service = CreateService(ManifestOnlyHandler(json), scope.Path);

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            service.CheckForUpdateAsync("1.0.0"));
    }

    [Theory]
    [InlineData("FiveMCleaner-Setup.exe")]
    [InlineData("FiveMCleaner-Setup-1.9.9-win-x64.exe")]
    [InlineData("FiveMCleaner-Setup-2.0.0-preview-win-x64.exe")]
    public async Task CheckForUpdate_RejectsGenericMismatchedAndPreviewAssetNames(string assetName)
    {
        using var scope = new TemporaryDirectory();
        var bytes = Encoding.UTF8.GetBytes("setup");
        var json = JsonSerializer.Serialize(new
        {
            draft = false,
            prerelease = false,
            tag_name = "v2.0.0",
            assets = new[]
            {
                new
                {
                    name = assetName,
                    state = "uploaded",
                    size = bytes.LongLength,
                    digest = $"sha256:{Sha256Hex(bytes)}",
                    browser_download_url =
                        $"https://github.com/marquezinii/FiveMCleaner/releases/download/v2.0.0/{assetName}",
                },
            },
        });
        using var service = CreateService(ManifestOnlyHandler(json), scope.Path);

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            service.CheckForUpdateAsync("1.0.0"));
    }

    [Theory]
    [InlineData("pending", 5, "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("uploaded", 0, "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("uploaded", 5000, "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("uploaded", 5, "md5:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("uploaded", 5, "sha256:abcd")]
    public async Task CheckForUpdate_RejectsInvalidStateSizeAndDigest(
        string state,
        long size,
        string digest)
    {
        using var scope = new TemporaryDirectory();
        var json = JsonSerializer.Serialize(new
        {
            draft = false,
            prerelease = false,
            tag_name = "v2.0.0",
            assets = new[]
            {
                new
                {
                    name = "FiveMCleaner-Setup-2.0.0-win-x64.exe",
                    state,
                    size,
                    digest,
                    browser_download_url = OfficialDownloadUrl("v2.0.0"),
                },
            },
        });
        using var service = CreateService(ManifestOnlyHandler(json), scope.Path);

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            service.CheckForUpdateAsync("1.0.0"));
    }

    [Theory]
    [InlineData("http://github.com/marquezinii/FiveMCleaner/releases/download/v2.0.0/FiveMCleaner-Setup-2.0.0-win-x64.exe")]
    [InlineData("https://evil.example/marquezinii/FiveMCleaner/releases/download/v2.0.0/FiveMCleaner-Setup-2.0.0-win-x64.exe")]
    [InlineData("https://github.com.evil.example/marquezinii/FiveMCleaner/releases/download/v2.0.0/FiveMCleaner-Setup-2.0.0-win-x64.exe")]
    [InlineData("https://github.com/marquezinii/Other/releases/download/v2.0.0/FiveMCleaner-Setup-2.0.0-win-x64.exe")]
    [InlineData("https://github.com/marquezinii/FiveMCleaner/releases/download/v9.0.0/FiveMCleaner-Setup-2.0.0-win-x64.exe")]
    [InlineData("https://github.com/marquezinii/FiveMCleaner/releases/download/v2.0.0/Other.exe")]
    [InlineData("https://github.com/marquezinii/FiveMCleaner/releases/download/v2.0.0/%2e%2e/FiveMCleaner-Setup-2.0.0-win-x64.exe")]
    [InlineData("https://github.com/marquezinii/FiveMCleaner/releases/download/v2.0.0/FiveMCleaner-Setup-2.0.0-win-x64.exe?download=1")]
    public async Task CheckForUpdate_RejectsUntrustedOrInconsistentAssetUrls(string url)
    {
        using var scope = new TemporaryDirectory();
        var installer = Encoding.UTF8.GetBytes("setup");
        var manifest = CreateManifest("v2.0.0", installer, downloadUrl: url);
        using var service = CreateService(ManifestOnlyHandler(manifest), scope.Path);

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            service.CheckForUpdateAsync("1.0.0"));
    }

    [Fact]
    public async Task CheckForUpdate_RejectsManifestRedirectEvenWhenDestinationUsesGithub()
    {
        using var scope = new TemporaryDirectory();
        var handler = new StubHttpMessageHandler((request, _) => Task.FromResult(
            RedirectResponse(request, "https://github.com/marquezinii/FiveMCleaner/releases/latest")));
        using var service = CreateService(handler, scope.Path);

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            service.CheckForUpdateAsync("1.0.0"));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CheckForUpdate_TimesOutAndPreservesCallerCancellationMeaning()
    {
        using var scope = new TemporaryDirectory();
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        });
        using var service = CreateService(
            handler,
            scope.Path,
            manifestTimeout: TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<TimeoutException>(() =>
            service.CheckForUpdateAsync("1.0.0"));

        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.CheckForUpdateAsync("1.0.0", cancellationSource.Token));
    }

    [Fact]
    public async Task DownloadUpdate_FollowsOnlyAllowlistedRedirectAndVerifiesBeforeAtomicMove()
    {
        using var scope = new TemporaryDirectory();
        var installer = Encoding.UTF8.GetBytes("a real, verified installer payload");
        var manifest = CreateManifest("v2.4.0", installer);
        var githubDownload = OfficialDownloadUrl("v2.4.0");
        var releaseAssetDownload = "https://release-assets.githubusercontent.com/release/asset?token=signed";
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri == GitHubReleaseUpdateService.LatestReleaseEndpoint)
            {
                return Task.FromResult(JsonResponse(request, manifest));
            }

            if (request.RequestUri?.AbsoluteUri == githubDownload)
            {
                Assert.Contains(
                    request.Headers.Accept,
                    value => value.MediaType == "application/octet-stream");
                Assert.Contains(
                    request.Headers.AcceptEncoding,
                    value => value.Value == "identity");
                return Task.FromResult(RedirectResponse(request, releaseAssetDownload));
            }

            Assert.Equal(releaseAssetDownload, request.RequestUri?.AbsoluteUri);
            return Task.FromResult(BytesResponse(request, installer));
        });
        using var service = CreateService(handler, scope.Path);
        var update = Assert.IsType<ReleaseUpdate>(await service.CheckForUpdateAsync("1.0.0"));
        var progressValues = new List<UpdateDownloadProgress>();

        var downloaded = await service.DownloadUpdateAsync(
            update,
            new InlineProgress<UpdateDownloadProgress>(progressValues.Add));

        Assert.False(downloaded.WasAlreadyDownloaded);
        Assert.Equal(
            Path.Combine(scope.Path, "2.4.0", "FiveMCleaner-Setup-2.4.0-win-x64.exe"),
            downloaded.InstallerPath);
        Assert.Equal(installer, await File.ReadAllBytesAsync(downloaded.InstallerPath));
        Assert.Empty(Directory.GetFiles(scope.Path, "*.part", SearchOption.AllDirectories));
        Assert.Equal(100, progressValues[^1].Percentage);

        var reused = await service.DownloadUpdateAsync(update);
        Assert.True(reused.WasAlreadyDownloaded);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Theory]
    [InlineData("https://evil.example/payload")]
    [InlineData("https://objects.githubusercontent.com/payload")]
    [InlineData("http://release-assets.githubusercontent.com/payload")]
    [InlineData("https://objects.githubusercontent.com.evil.example/payload")]
    [InlineData("https://release-assets.githubusercontent.com.evil.example/payload")]
    [InlineData("https://sub.release-assets.githubusercontent.com/payload")]
    public async Task DownloadUpdate_RejectsRedirectOutsideExactHostAllowlist(string redirectUrl)
    {
        using var scope = new TemporaryDirectory();
        var installer = Encoding.UTF8.GetBytes("verified installer");
        var manifest = CreateManifest("v2.0.0", installer);
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri == GitHubReleaseUpdateService.LatestReleaseEndpoint)
            {
                return Task.FromResult(JsonResponse(request, manifest));
            }

            return Task.FromResult(RedirectResponse(request, redirectUrl));
        });
        using var service = CreateService(handler, scope.Path);
        var update = Assert.IsType<ReleaseUpdate>(await service.CheckForUpdateAsync("1.0.0"));

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            service.DownloadUpdateAsync(update));
        Assert.Empty(Directory.GetFiles(scope.Path, "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DownloadUpdate_RejectsHashMismatchAndRemovesPartialFile()
    {
        using var scope = new TemporaryDirectory();
        var expected = Encoding.UTF8.GetBytes("expected bytes");
        var tampered = Encoding.UTF8.GetBytes("tampered bytes");
        Assert.Equal(expected.Length, tampered.Length);
        var handler = ManifestAndDownloadHandler(CreateManifest("v2.0.0", expected), tampered);
        using var service = CreateService(handler, scope.Path);
        var update = Assert.IsType<ReleaseUpdate>(await service.CheckForUpdateAsync("1.0.0"));

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            service.DownloadUpdateAsync(update));

        Assert.False(File.Exists(Path.Combine(scope.Path, "2.0.0", "FiveMCleaner-Setup-2.0.0-win-x64.exe")));
        Assert.Empty(Directory.GetFiles(scope.Path, "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DownloadUpdate_RejectsContentLengthMismatchBeforeWriting()
    {
        using var scope = new TemporaryDirectory();
        var expected = Encoding.UTF8.GetBytes("expected");
        var tooLarge = Encoding.UTF8.GetBytes("expected-extra");
        using var service = CreateService(
            ManifestAndDownloadHandler(CreateManifest("v2.0.0", expected), tooLarge),
            scope.Path);
        var update = Assert.IsType<ReleaseUpdate>(await service.CheckForUpdateAsync("1.0.0"));

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            service.DownloadUpdateAsync(update));
        Assert.Empty(Directory.GetFiles(scope.Path, "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DownloadUpdate_CancellationRemovesPartialFile()
    {
        using var scope = new TemporaryDirectory();
        var installer = Enumerable.Range(0, 128).Select(value => (byte)value).ToArray();
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri == GitHubReleaseUpdateService.LatestReleaseEndpoint)
            {
                return Task.FromResult(JsonResponse(request, CreateManifest("v2.0.0", installer)));
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StreamContent(new OneByteAtATimeStream(installer)),
            };
            return Task.FromResult(response);
        });
        using var service = CreateService(handler, scope.Path);
        var update = Assert.IsType<ReleaseUpdate>(await service.CheckForUpdateAsync("1.0.0"));
        using var cancellationSource = new CancellationTokenSource();
        var progress = new InlineProgress<UpdateDownloadProgress>(_ => cancellationSource.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.DownloadUpdateAsync(update, progress, cancellationSource.Token));

        Assert.Empty(Directory.GetFiles(scope.Path, "*.part", SearchOption.AllDirectories));
        Assert.False(File.Exists(Path.Combine(scope.Path, "2.0.0", "FiveMCleaner-Setup-2.0.0-win-x64.exe")));
    }

    [Theory]
    [InlineData("../FiveMCleaner-{version}.exe")]
    [InlineData("sub/FiveMCleaner-{version}.exe")]
    [InlineData("FiveMCleaner-{version}.exe:evil")]
    [InlineData("not-an-installer-{version}.zip")]
    [InlineData("FiveMCleaner.exe")]
    [InlineData("FiveMCleaner-{version}-{version}.exe")]
    public void Constructor_RejectsUnsafeAllowlistTemplates(string assetNameTemplate)
    {
        using var scope = new TemporaryDirectory();
        var handler = ManifestOnlyHandler("{}");
        var options = CreateOptions(scope.Path, assetNameTemplate);

        Assert.Throws<ArgumentException>(() =>
            new GitHubReleaseUpdateService(handler, options));
    }

    [Fact]
    public async Task DownloadUpdate_RevalidatesForgedPackageBeforeCreatingDirectories()
    {
        using var scope = new TemporaryDirectory();
        using var service = CreateService(ManifestOnlyHandler("{}"), scope.Path);
        var forged = new ReleaseUpdate(
            StableSemanticVersion.Parse("2.0.0"),
            "v2.0.0",
            "../FiveMCleaner-Setup-2.0.0-win-x64.exe",
            new Uri(OfficialDownloadUrl("v2.0.0")),
            5,
            new string('a', 64));

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            service.DownloadUpdateAsync(forged));
        Assert.Empty(Directory.GetFileSystemEntries(scope.Path));
    }

    [Fact]
    public async Task ManifestSizeLimitIsEnforcedWithoutTrustingContentLength()
    {
        using var scope = new TemporaryDirectory();
        var oversizedJson = "{" + new string(' ', 5000) + "}";
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(oversizedJson))),
            };
            return Task.FromResult(response);
        });
        using var service = CreateService(handler, scope.Path, maximumManifestSize: 1024);

        await Assert.ThrowsAsync<UpdateSecurityException>(() =>
            service.CheckForUpdateAsync("1.0.0"));
    }

    private static GitHubReleaseUpdateService CreateService(
        HttpMessageHandler handler,
        string updatesRoot,
        string? assetNameTemplate = null,
        TimeSpan? manifestTimeout = null,
        int maximumManifestSize = 16 * 1024) => new(
            handler,
            CreateOptions(
                updatesRoot,
                assetNameTemplate,
                manifestTimeout,
                maximumManifestSize));

    private static GitHubReleaseUpdateOptions CreateOptions(
        string updatesRoot,
        string? assetNameTemplate = null,
        TimeSpan? manifestTimeout = null,
        int maximumManifestSize = 16 * 1024) => new()
        {
            InstallerAssetNameTemplate = assetNameTemplate
                ?? "FiveMCleaner-Setup-{version}-win-x64.exe",
            UpdatesRootDirectory = updatesRoot,
            MinimumInstallerSizeBytes = 1,
            MaximumInstallerSizeBytes = 4096,
            MaximumManifestSizeBytes = maximumManifestSize,
            MaximumRedirects = 3,
            ManifestTimeout = manifestTimeout ?? TimeSpan.FromSeconds(5),
            DownloadTimeout = TimeSpan.FromSeconds(5),
        };

    private static StubHttpMessageHandler ManifestOnlyHandler(string manifest) =>
        new((request, _) => Task.FromResult(JsonResponse(request, manifest)));

    private static StubHttpMessageHandler ManifestAndDownloadHandler(
        string manifest,
        byte[] download) => new((request, _) =>
    {
        return Task.FromResult(request.RequestUri == GitHubReleaseUpdateService.LatestReleaseEndpoint
            ? JsonResponse(request, manifest)
            : BytesResponse(request, download));
    });

    private static HttpResponseMessage JsonResponse(HttpRequestMessage request, string json) => new(
        HttpStatusCode.OK)
    {
        RequestMessage = request,
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage BytesResponse(HttpRequestMessage request, byte[] bytes) => new(
        HttpStatusCode.OK)
    {
        RequestMessage = request,
        Content = new ByteArrayContent(bytes),
    };

    private static HttpResponseMessage RedirectResponse(HttpRequestMessage request, string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            RequestMessage = request,
        };
        response.Headers.Location = new Uri(location, UriKind.Absolute);
        return response;
    }

    private static string CreateManifest(
        string tag,
        byte[] installer,
        bool draft = false,
        bool prerelease = false,
        string? downloadUrl = null,
        string? releaseNotesUrl = null) => JsonSerializer.Serialize(new
        {
            draft,
            prerelease,
            tag_name = tag,
            html_url = releaseNotesUrl ?? OfficialReleaseNotesUrl(tag),
            assets = new[]
            {
                new
                {
                    name = AssetName(tag),
                    state = "uploaded",
                    size = installer.LongLength,
                    digest = $"sha256:{Sha256Hex(installer)}",
                    browser_download_url = downloadUrl ?? OfficialDownloadUrl(tag),
                },
            },
        });

    private static string CreateAssetJson(string tag, byte[] installer) => JsonSerializer.Serialize(new
    {
        name = AssetName(tag),
        state = "uploaded",
        size = installer.LongLength,
        digest = $"sha256:{Sha256Hex(installer)}",
        browser_download_url = OfficialDownloadUrl(tag),
    });

    private static string OfficialDownloadUrl(string tag) =>
        $"https://github.com/marquezinii/FiveMCleaner/releases/download/{tag}/{AssetName(tag)}";

    private static string OfficialReleaseNotesUrl(string tag) =>
        $"https://github.com/marquezinii/FiveMCleaner/releases/tag/{tag}";

    private static string AssetName(string tag)
    {
        return StableSemanticVersion.TryParse(tag, out var version)
            ? $"FiveMCleaner-Setup-{version.CoreVersion}-win-x64.exe"
            : "FiveMCleaner-Setup-2.0.0-win-x64.exe";
    }

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder =
            responder;

        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return responder(request, cancellationToken);
        }
    }

    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }

    private sealed class OneByteAtATimeStream(byte[] bytes) : Stream
    {
        private int position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => bytes.Length;

        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (position >= bytes.Length)
            {
                return 0;
            }

            buffer[offset] = bytes[position++];
            return 1;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (position >= bytes.Length)
            {
                return ValueTask.FromResult(0);
            }

            buffer.Span[0] = bytes[position++];
            return ValueTask.FromResult(1);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"FiveMCleaner.Updater.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
