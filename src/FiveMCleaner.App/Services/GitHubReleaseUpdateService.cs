using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FiveMCleaner.App.Services;

/// <summary>
/// Reads stable public releases from the official FiveMCleaner repository and
/// downloads a hash-verified installer. This service never starts the installer.
/// </summary>
public sealed class GitHubReleaseUpdateService : IReleaseUpdateService, IDisposable
{
    internal static readonly Uri LatestReleaseEndpoint = new(
        "https://api.github.com/repos/marquezinii/FiveMCleaner/releases/latest");

    private const string RepositoryReleasePrefix =
        "/marquezinii/FiveMCleaner/releases/download/";

    private static readonly Regex Sha256DigestPattern = new(
        "^sha256:(?<hash>[0-9a-fA-F]{64})$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromMilliseconds(100));

    private readonly HttpClient httpClient;
    private readonly string installerAssetNameTemplate;
    private readonly string updatesRootDirectory;
    private readonly long minimumInstallerSizeBytes;
    private readonly long maximumInstallerSizeBytes;
    private readonly int maximumManifestSizeBytes;
    private readonly int maximumRedirects;
    private readonly TimeSpan manifestTimeout;
    private readonly TimeSpan downloadTimeout;
    private bool disposed;

    public GitHubReleaseUpdateService(GitHubReleaseUpdateOptions? options = null)
        : this(CreateProductionHandler(), options)
    {
    }

    internal GitHubReleaseUpdateService(
        HttpMessageHandler messageHandler,
        GitHubReleaseUpdateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(messageHandler);
        options ??= new GitHubReleaseUpdateOptions();

        installerAssetNameTemplate = ValidateAssetNameTemplate(options.InstallerAssetNameTemplate);
        minimumInstallerSizeBytes = options.MinimumInstallerSizeBytes;
        maximumInstallerSizeBytes = options.MaximumInstallerSizeBytes;
        maximumManifestSizeBytes = options.MaximumManifestSizeBytes;
        maximumRedirects = options.MaximumRedirects;
        manifestTimeout = options.ManifestTimeout;
        downloadTimeout = options.DownloadTimeout;

        if (minimumInstallerSizeBytes <= 0
            || maximumInstallerSizeBytes < minimumInstallerSizeBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Os limites de tamanho do instalador sao invalidos.");
        }

        if (maximumManifestSizeBytes is < 1024 or > 16 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "O limite do manifesto precisa estar entre 1 KiB e 16 MiB.");
        }

        if (maximumRedirects is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "O limite de redirecionamentos precisa estar entre zero e dez.");
        }

        ValidateTimeout(manifestTimeout, nameof(options.ManifestTimeout));
        ValidateTimeout(downloadTimeout, nameof(options.DownloadTimeout));

        updatesRootDirectory = ResolveUpdatesRoot(options.UpdatesRootDirectory);

        httpClient = new HttpClient(messageHandler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("FiveMCleaner-Updater", "1.0"));
    }

    public Task<ReleaseUpdate?> CheckForUpdateAsync(
        string currentVersion,
        CancellationToken cancellationToken = default) =>
        CheckForUpdateAsync(StableSemanticVersion.Parse(currentVersion), cancellationToken);

    public Task<ReleaseUpdate?> CheckForUpdateAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default) =>
        CheckForUpdateAsync(StableSemanticVersion.FromVersion(currentVersion), cancellationToken);

    public async Task<ReleaseUpdate?> CheckForUpdateAsync(
        StableSemanticVersion currentVersion,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(currentVersion);

        using var timeoutSource = CreateTimeoutSource(cancellationToken, manifestTimeout);
        try
        {
            using var response = await SendWithRedirectValidationAsync(
                LatestReleaseEndpoint,
                RequestPurpose.Manifest,
                timeoutSource.Token).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new HttpRequestException(
                    $"O GitHub respondeu com HTTP {(int)response.StatusCode} ao consultar atualizacoes.",
                    inner: null,
                    response.StatusCode);
            }

            using var document = await ReadManifestAsync(
                response.Content,
                timeoutSource.Token).ConfigureAwait(false);
            var update = ParseManifest(document.RootElement);
            return update.Version.CompareTo(currentVersion) > 0
                ? update
                : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("A consulta de atualizacoes excedeu o tempo limite.");
        }
    }

    public async Task<DownloadedUpdate> DownloadUpdateAsync(
        ReleaseUpdate update,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(update);

        var validatedUpdate = ValidateUpdateForDownload(update);
        var versionDirectory = GetContainedPath(
            updatesRootDirectory,
            validatedUpdate.Version.CoreVersion);
        var finalPath = GetContainedPath(versionDirectory, validatedUpdate.AssetName);
        Directory.CreateDirectory(versionDirectory);

        if (await ExistingFileMatchesAsync(
                finalPath,
                validatedUpdate.SizeBytes,
                validatedUpdate.Sha256Bytes,
                cancellationToken).ConfigureAwait(false))
        {
            TryWriteMarkOfTheWeb(finalPath, validatedUpdate.DownloadUri);
            progress?.Report(new UpdateDownloadProgress(
                validatedUpdate.SizeBytes,
                validatedUpdate.SizeBytes));
            return new DownloadedUpdate(
                validatedUpdate.Version,
                finalPath,
                validatedUpdate.SizeBytes,
                WasAlreadyDownloaded: true);
        }

        var temporaryPath = GetContainedPath(
            versionDirectory,
            $"{validatedUpdate.AssetName}.{Guid.NewGuid():N}.part");

        using var timeoutSource = CreateTimeoutSource(cancellationToken, downloadTimeout);
        try
        {
            using var response = await SendWithRedirectValidationAsync(
                validatedUpdate.DownloadUri,
                RequestPurpose.Installer,
                timeoutSource.Token).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new HttpRequestException(
                    $"O GitHub respondeu com HTTP {(int)response.StatusCode} ao baixar a atualizacao.",
                    inner: null,
                    response.StatusCode);
            }

            ValidateContentLength(response.Content.Headers.ContentLength, validatedUpdate.SizeBytes);
            await DownloadAndVerifyAsync(
                response.Content,
                temporaryPath,
                validatedUpdate.SizeBytes,
                validatedUpdate.Sha256Bytes,
                progress,
                timeoutSource.Token).ConfigureAwait(false);

            File.Move(temporaryPath, finalPath, overwrite: true);
            TryWriteMarkOfTheWeb(finalPath, validatedUpdate.DownloadUri);
            return new DownloadedUpdate(
                validatedUpdate.Version,
                finalPath,
                validatedUpdate.SizeBytes,
                WasAlreadyDownloaded: false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("O download da atualizacao excedeu o tempo limite.");
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        httpClient.Dispose();
    }

    private static HttpMessageHandler CreateProductionHandler() => new HttpClientHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        CheckCertificateRevocationList = true,
        UseCookies = false,
    };

    private static string ValidateAssetNameTemplate(string? template)
    {
        const string token = "{version}";
        if (string.IsNullOrWhiteSpace(template)
            || template.Length > 160
            || template.IndexOf(token, StringComparison.Ordinal) < 0
            || template.IndexOf(token, StringComparison.Ordinal)
                != template.LastIndexOf(token, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "O template do instalador precisa conter exatamente um token {version}.",
                nameof(template));
        }

        var sampleName = template.Replace(token, "0.0.0", StringComparison.Ordinal);
        if (sampleName.Contains('{')
            || sampleName.Contains('}')
            || !IsSafeInstallerName(sampleName))
        {
            throw new ArgumentException(
                "O template precisa produzir apenas um nome de arquivo .exe seguro.",
                nameof(template));
        }

        return template;
    }

    private static bool IsSafeInstallerName(string? assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName)
            || assetName.Length > 128
            || !assetName.Equals(assetName.Trim(), StringComparison.Ordinal)
            || !assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(assetName).Equals(assetName, StringComparison.Ordinal)
            || assetName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || assetName is "." or "..")
        {
            return false;
        }

        return !assetName.Contains('/') && !assetName.Contains('\\');
    }

    private static void ValidateTimeout(TimeSpan timeout, string parameterName)
    {
        if (timeout < TimeSpan.FromMilliseconds(100)
            || timeout > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "O timeout precisa estar entre 100 ms e uma hora.");
        }
    }

    private static string ResolveUpdatesRoot(string? configuredRoot)
    {
        var root = configuredRoot;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FiveMCleaner",
                "Updates");
        }

        if (!Path.IsPathFullyQualified(root))
        {
            throw new ArgumentException("A pasta de atualizacoes precisa usar um caminho absoluto.", nameof(configuredRoot));
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    private static CancellationTokenSource CreateTimeoutSource(
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(timeout);
        return source;
    }

    private async Task<HttpResponseMessage> SendWithRedirectValidationAsync(
        Uri initialUri,
        RequestPurpose purpose,
        CancellationToken cancellationToken)
    {
        var currentUri = initialUri;
        for (var redirectCount = 0; ; redirectCount++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            ConfigureRequest(request, purpose);
            var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            try
            {
                ValidateEffectiveResponseUri(response, currentUri, purpose);
            }
            catch
            {
                response.Dispose();
                throw;
            }
            if (!IsRedirect(response.StatusCode))
            {
                return response;
            }

            if (redirectCount >= maximumRedirects)
            {
                response.Dispose();
                throw new UpdateSecurityException("A resposta excedeu o limite de redirecionamentos.");
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null)
            {
                throw new UpdateSecurityException("O redirecionamento nao informou um destino.");
            }

            if (purpose == RequestPurpose.Manifest)
            {
                throw new UpdateSecurityException("Redirecionamentos do manifesto de atualizacao nao sao aceitos.");
            }

            currentUri = location.IsAbsoluteUri
                ? location
                : new Uri(currentUri, location);
            ValidateInstallerRedirectUri(currentUri);
        }
    }

    private static void ConfigureRequest(HttpRequestMessage request, RequestPurpose purpose)
    {
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        if (purpose == RequestPurpose.Manifest)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            return;
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
    }

    private static void ValidateEffectiveResponseUri(
        HttpResponseMessage response,
        Uri expectedRequestUri,
        RequestPurpose purpose)
    {
        var effectiveUri = response.RequestMessage?.RequestUri;
        if (effectiveUri is null
            || effectiveUri.Equals(expectedRequestUri))
        {
            return;
        }

        if (purpose == RequestPurpose.Manifest)
        {
            throw new UpdateSecurityException("O transporte redirecionou o manifesto sem autorizacao.");
        }

        ValidateInstallerRedirectUri(effectiveUri);
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently
        or HttpStatusCode.Redirect
        or HttpStatusCode.RedirectMethod
        or HttpStatusCode.TemporaryRedirect
        or HttpStatusCode.PermanentRedirect;

    private static void ValidateInstallerRedirectUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.Ordinal)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !(uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new UpdateSecurityException("O download tentou sair dos hosts HTTPS permitidos do GitHub.");
        }
    }

    private async Task<JsonDocument> ReadManifestAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long contentLength
            && contentLength > maximumManifestSizeBytes)
        {
            throw new UpdateSecurityException("O manifesto de atualizacao excede o limite permitido.");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            var total = 0;
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total = checked(total + read);
                if (total > maximumManifestSizeBytes)
                {
                    throw new UpdateSecurityException("O manifesto de atualizacao excede o limite permitido.");
                }

                memory.Write(buffer, 0, read);
            }

            memory.Position = 0;
            try
            {
                return await JsonDocument.ParseAsync(
                    memory,
                    new JsonDocumentOptions
                    {
                        AllowTrailingCommas = false,
                        CommentHandling = JsonCommentHandling.Disallow,
                        MaxDepth = 32,
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException exception)
            {
                throw new UpdateSecurityException($"O manifesto de atualizacao e invalido: {exception.Message}");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private ReleaseUpdate ParseManifest(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new UpdateSecurityException("O manifesto de atualizacao nao e um objeto JSON.");
        }

        if (GetRequiredBoolean(root, "draft"))
        {
            throw new UpdateSecurityException("Releases em rascunho nao sao aceitas.");
        }

        if (GetRequiredBoolean(root, "prerelease"))
        {
            throw new UpdateSecurityException("Pre-releases nao sao aceitas pelo canal estavel.");
        }

        var tagName = GetRequiredString(root, "tag_name");
        if (!StableSemanticVersion.TryParse(tagName, out var version))
        {
            throw new UpdateSecurityException("A tag da release nao e um SemVer estavel valido.");
        }

        if (!tagName.Equals($"v{version.CoreVersion}", StringComparison.Ordinal))
        {
            throw new UpdateSecurityException("A tag estavel precisa usar exatamente o formato vX.Y.Z.");
        }

        var assets = GetRequiredProperty(root, "assets");
        if (assets.ValueKind != JsonValueKind.Array)
        {
            throw new UpdateSecurityException("A lista de assets da release e invalida.");
        }

        var selectedName = BuildExpectedAssetName(version);
        JsonElement? selectedAsset = null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var nameProperty = TryGetUniqueProperty(asset, "name");
            if (nameProperty is not JsonElement nameElement
                || nameElement.ValueKind != JsonValueKind.String
                || !selectedName.Equals(nameElement.GetString(), StringComparison.Ordinal))
            {
                continue;
            }

            if (selectedAsset is not null)
            {
                throw new UpdateSecurityException("A release contem assets de instalador duplicados.");
            }

            selectedAsset = asset;
        }

        if (selectedAsset is not JsonElement assetElement)
        {
            throw new UpdateSecurityException("A release nao contem um instalador permitido.");
        }

        if (!GetRequiredString(assetElement, "state").Equals("uploaded", StringComparison.Ordinal))
        {
            throw new UpdateSecurityException("O asset do instalador ainda nao esta completamente publicado.");
        }

        var size = GetRequiredInt64(assetElement, "size");
        ValidateInstallerSize(size);

        var digest = GetRequiredString(assetElement, "digest");
        var hash = ParseSha256Digest(digest);

        var downloadUrl = GetRequiredString(assetElement, "browser_download_url");
        var downloadUri = ValidateBrowserDownloadUri(downloadUrl, tagName, selectedName);
        Uri? releaseNotesUri = null;
        var releaseNotesElement = TryGetUniqueProperty(root, "html_url");
        if (releaseNotesElement is JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                throw new UpdateSecurityException("A pagina de notas da release e invalida.");
            }

            releaseNotesUri = ValidateReleaseNotesUri(
                element.GetString() ?? string.Empty,
                tagName);
        }

        return new ReleaseUpdate(
            version,
            tagName,
            selectedName,
            downloadUri,
            size,
            hash,
            releaseNotesUri);
    }

    private ValidatedUpdate ValidateUpdateForDownload(ReleaseUpdate update)
    {
        if (!StableSemanticVersion.TryParse(update.TagName, out var parsedVersion)
            || parsedVersion.CompareTo(update.Version) != 0
            || !parsedVersion.ToString().Equals(update.Version.ToString(), StringComparison.Ordinal))
        {
            throw new UpdateSecurityException("A versao do pacote de atualizacao e inconsistente.");
        }

        var expectedAssetName = BuildExpectedAssetName(parsedVersion);
        if (!expectedAssetName.Equals(update.AssetName, StringComparison.Ordinal)
            || !IsSafeInstallerName(update.AssetName))
        {
            throw new UpdateSecurityException("O pacote nao pertence a allowlist de instaladores.");
        }

        ValidateInstallerSize(update.SizeBytes);
        var sha256Hex = ParseSha256Digest($"sha256:{update.Sha256Hex}");
        var sha256Bytes = Convert.FromHexString(sha256Hex);
        var downloadUri = ValidateBrowserDownloadUri(
            update.DownloadUri.OriginalString,
            update.TagName,
            update.AssetName);

        return new ValidatedUpdate(
            parsedVersion,
            update.TagName,
            update.AssetName,
            downloadUri,
            update.SizeBytes,
            sha256Bytes);
    }

    private string BuildExpectedAssetName(StableSemanticVersion version)
    {
        var assetName = installerAssetNameTemplate.Replace(
            "{version}",
            version.CoreVersion,
            StringComparison.Ordinal);
        if (!IsSafeInstallerName(assetName))
        {
            throw new UpdateSecurityException("O nome derivado do instalador nao e seguro.");
        }

        return assetName;
    }

    private void ValidateInstallerSize(long size)
    {
        if (size < minimumInstallerSizeBytes || size > maximumInstallerSizeBytes)
        {
            throw new UpdateSecurityException("O tamanho declarado do instalador esta fora dos limites permitidos.");
        }
    }

    private static string ParseSha256Digest(string digest)
    {
        var match = Sha256DigestPattern.Match(digest);
        if (!match.Success)
        {
            throw new UpdateSecurityException("A release nao possui um digest SHA-256 valido.");
        }

        return match.Groups["hash"].Value.ToLowerInvariant();
    }

    private static Uri ValidateBrowserDownloadUri(
        string downloadUrl,
        string tagName,
        string assetName)
    {
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.Ordinal)
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new UpdateSecurityException("A URL do instalador nao e uma URL HTTPS publica do repositorio oficial.");
        }

        string decodedPath;
        try
        {
            decodedPath = Uri.UnescapeDataString(uri.AbsolutePath);
        }
        catch (UriFormatException)
        {
            throw new UpdateSecurityException("A URL do instalador possui uma codificacao invalida.");
        }

        var expectedPath = $"{RepositoryReleasePrefix}{tagName}/{assetName}";
        if (!decodedPath.Equals(expectedPath, StringComparison.Ordinal))
        {
            throw new UpdateSecurityException("A URL do instalador nao aponta para o asset esperado da release.");
        }

        return uri;
    }

    private static Uri ValidateReleaseNotesUri(string releaseNotesUrl, string tagName)
    {
        if (!Uri.TryCreate(releaseNotesUrl, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.Ordinal)
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !uri.AbsolutePath.Equals(
                $"/marquezinii/FiveMCleaner/releases/tag/{tagName}",
                StringComparison.Ordinal)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new UpdateSecurityException("A pagina de notas da release nao pertence ao repositorio oficial.");
        }

        return uri;
    }

    private static JsonElement GetRequiredProperty(JsonElement element, string propertyName)
    {
        var property = TryGetUniqueProperty(element, propertyName);
        return property
            ?? throw new UpdateSecurityException($"O manifesto nao possui a propriedade obrigatoria '{propertyName}'.");
    }

    private static JsonElement? TryGetUniqueProperty(JsonElement element, string propertyName)
    {
        JsonElement? result = null;
        foreach (var property in element.EnumerateObject())
        {
            if (!property.NameEquals(propertyName))
            {
                continue;
            }

            if (result is not null)
            {
                throw new UpdateSecurityException($"O manifesto repete a propriedade '{propertyName}'.");
            }

            result = property.Value;
        }

        return result;
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        var property = GetRequiredProperty(element, propertyName);
        var value = property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
        if (string.IsNullOrEmpty(value))
        {
            throw new UpdateSecurityException($"A propriedade '{propertyName}' precisa ser uma string nao vazia.");
        }

        return value;
    }

    private static bool GetRequiredBoolean(JsonElement element, string propertyName)
    {
        var property = GetRequiredProperty(element, propertyName);
        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new UpdateSecurityException($"A propriedade '{propertyName}' precisa ser booleana."),
        };
    }

    private static long GetRequiredInt64(JsonElement element, string propertyName)
    {
        var property = GetRequiredProperty(element, propertyName);
        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt64(out var value))
        {
            throw new UpdateSecurityException($"A propriedade '{propertyName}' precisa ser um inteiro de 64 bits.");
        }

        return value;
    }

    private static void ValidateContentLength(long? contentLength, long expectedSize)
    {
        if (contentLength is long declaredLength && declaredLength != expectedSize)
        {
            throw new UpdateSecurityException("O tamanho HTTP do instalador difere do manifesto da release.");
        }
    }

    private static async Task DownloadAndVerifyAsync(
        HttpContent content,
        string temporaryPath,
        long expectedSize,
        byte[] expectedHash,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        long total = 0;
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total = checked(total + read);
                if (total > expectedSize)
                {
                    throw new UpdateSecurityException("O instalador recebido excede o tamanho declarado.");
                }

                hasher.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                progress?.Report(new UpdateDownloadProgress(total, expectedSize));
            }

            if (total != expectedSize)
            {
                throw new UpdateSecurityException("O instalador recebido esta truncado.");
            }

            var actualHash = hasher.GetHashAndReset();
            if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
            {
                throw new UpdateSecurityException("O SHA-256 do instalador nao corresponde ao digest publicado.");
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            destination.Flush(flushToDisk: true);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static async Task<bool> ExistingFileMatchesAsync(
        string path,
        long expectedSize,
        byte[] expectedHash,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var fileInfo = new FileInfo(path);
        if (fileInfo.Length != expectedSize)
        {
            return false;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actualHash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static string GetContainedPath(string root, string childName)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, childName));
        var requiredPrefix = fullRoot + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateSecurityException("O caminho da atualizacao tentou sair da pasta permitida.");
        }

        return candidate;
    }

    private static void TryWriteMarkOfTheWeb(string installerPath, Uri sourceUri)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var contents = string.Join(
                "\r\n",
                "[ZoneTransfer]",
                "ZoneId=3",
                $"ReferrerUrl={sourceUri.GetLeftPart(UriPartial.Authority)}",
                $"HostUrl={sourceUri}",
                string.Empty);
            File.WriteAllText(
                installerPath + ":Zone.Identifier",
                contents,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception exception) when (exception is
            IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            // Best effort: filesystems without alternate data streams remain usable.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Preserve the original validation/download exception.
        }
        catch (UnauthorizedAccessException)
        {
            // Preserve the original validation/download exception.
        }
    }

    private enum RequestPurpose
    {
        Manifest,
        Installer,
    }

    private sealed record ValidatedUpdate(
        StableSemanticVersion Version,
        string TagName,
        string AssetName,
        Uri DownloadUri,
        long SizeBytes,
        byte[] Sha256Bytes);
}
