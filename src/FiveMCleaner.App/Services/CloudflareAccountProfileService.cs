using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace FiveMCleaner.App.Services;

/// <summary>
/// Sends the profile-completion request to the Cloudflare Worker's
/// <c>POST /account/profile</c> route, authenticated with the caller's
/// fresh Firebase ID token. Validation mirrors the server (see
/// <c>infra/cloudflare-worker/src/auth/accountProfile.js</c>), which
/// re-validates and is the only source of truth for username uniqueness.
/// </summary>
public sealed class CloudflareAccountProfileService : IAccountProfileService
{
    private static readonly HttpClient SharedClient = CreateClient();
    private readonly HttpClient httpClient;
    private readonly Uri endpoint;

    public CloudflareAccountProfileService(Uri endpoint)
        : this(SharedClient, endpoint)
    {
    }

    internal CloudflareAccountProfileService(HttpClient httpClient, Uri endpoint)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Endpoint de perfil de conta inválido.", nameof(endpoint));
        }

        this.endpoint = endpoint;
    }

    public async Task<AccountProfileResult> CreateAsync(
        string idToken,
        AccountProfileSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idToken);
        ArgumentNullException.ThrowIfNull(submission);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new
            {
                username = submission.Username,
                firstName = submission.FirstName,
                lastName = submission.LastName,
                termsVersion = submission.TermsVersion,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return new AccountProfileResult(
                AccountProfileOutcome.Failed,
                "Não foi possível conectar para salvar seu perfil. Verifique sua conexão.");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                return new AccountProfileResult(
                    AccountProfileOutcome.UsernameTaken,
                    "Este nome de usuário já está em uso. Escolha outro.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new AccountProfileResult(
                    AccountProfileOutcome.Failed,
                    $"Não foi possível salvar seu perfil agora (erro {(int)response.StatusCode}). Tente novamente.");
            }

            return new AccountProfileResult(AccountProfileOutcome.Created, null);
        }
    }

    public async Task<AccountProfileFetchResult> FetchAsync(
        string idToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return new AccountProfileFetchResult(AccountProfileFetchOutcome.Failed);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new AccountProfileFetchResult(AccountProfileFetchOutcome.NotFound);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new AccountProfileFetchResult(AccountProfileFetchOutcome.Failed);
            }

            AccountProfileResponseDto? body;
            try
            {
                body = await response.Content
                    .ReadFromJsonAsync<AccountProfileResponseDto>(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (System.Text.Json.JsonException)
            {
                return new AccountProfileFetchResult(AccountProfileFetchOutcome.Failed);
            }

            if (body is null || string.IsNullOrWhiteSpace(body.FirstName))
            {
                return new AccountProfileFetchResult(AccountProfileFetchOutcome.Failed);
            }

            return new AccountProfileFetchResult(
                AccountProfileFetchOutcome.Found,
                body.Username,
                body.FirstName,
                body.LastName,
                body.TermsVersion);
        }
    }

    public async Task<AccountProfileDeletionResult> DeleteAsync(
        string idToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idToken);

        using var request = new HttpRequestMessage(HttpMethod.Delete, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

        try
        {
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            return new AccountProfileDeletionResult(
                response.IsSuccessStatusCode ? AccountProfileDeletionOutcome.Deleted : AccountProfileDeletionOutcome.Failed);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return new AccountProfileDeletionResult(AccountProfileDeletionOutcome.Failed);
        }
    }

    public async Task<UsernameAvailability> CheckUsernameAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        // Mirrors the server's own rule (accountProfile.js) so a name that
        // could never be accepted is reported locally, without a round trip.
        if (!AccountValidation.IsValidUsername(username))
        {
            return UsernameAvailability.Invalid;
        }

        // "…/account/profile" + "username-available" resolves to
        // "…/account/username-available": the probe always follows whichever
        // Worker origin the profile endpoint was configured with.
        var probe = new Uri(new Uri(endpoint, "username-available"), $"?u={Uri.EscapeDataString(username.Trim())}");

        HttpResponseMessage response;
        try
        {
            response = await httpClient
                .GetAsync(probe, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return UsernameAvailability.Unknown;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // 400 means the server disagrees with our local rule, 429
                // means we asked too often -- neither is evidence the name
                // is free, so both stay Unknown/Invalid rather than green.
                return response.StatusCode == HttpStatusCode.BadRequest
                    ? UsernameAvailability.Invalid
                    : UsernameAvailability.Unknown;
            }

            UsernameAvailabilityDto? body;
            try
            {
                body = await response.Content
                    .ReadFromJsonAsync<UsernameAvailabilityDto>(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (System.Text.Json.JsonException)
            {
                return UsernameAvailability.Unknown;
            }

            if (body?.Available is not { } available)
            {
                return UsernameAvailability.Unknown;
            }

            return available ? UsernameAvailability.Available : UsernameAvailability.Taken;
        }
    }

    private sealed record UsernameAvailabilityDto(
        [property: JsonPropertyName("available")] bool? Available);

    private sealed record AccountProfileResponseDto(
        [property: JsonPropertyName("username")] string? Username,
        [property: JsonPropertyName("firstName")] string? FirstName,
        [property: JsonPropertyName("lastName")] string? LastName,
        [property: JsonPropertyName("termsVersion")] string? TermsVersion);

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
    }
}
