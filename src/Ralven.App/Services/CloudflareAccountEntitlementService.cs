using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ralven.App.Services;

public enum AccountEntitlementTier
{
    Unavailable,
    Free,
    Pro,
}

public sealed record AccountEntitlementSnapshot(
    AccountEntitlementTier Tier,
    DateTimeOffset? ValidUntil = null,
    bool HasRalvenAi = false);

/// <summary>
/// Reads the caller's server-authoritative access snapshot. It never grants
/// access from local state and deliberately keeps no persistent cache.
/// </summary>
public sealed class CloudflareAccountEntitlementService
{
    private const string ProEntitlement = "ralven_pro";
    private const string AiEntitlement = "ralven_ai";
    private static readonly HttpClient SharedClient =
        CloudflareTransportDefaults.CreateClient(TimeSpan.FromSeconds(20));

    private readonly HttpClient httpClient;
    private readonly Uri endpoint;
    private readonly TimeProvider timeProvider;

    public CloudflareAccountEntitlementService(Uri accountProfileEndpoint)
        : this(SharedClient, accountProfileEndpoint, TimeProvider.System)
    {
    }

    internal CloudflareAccountEntitlementService(
        HttpClient httpClient,
        Uri accountProfileEndpoint,
        TimeProvider? timeProvider = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        CloudflareTransportDefaults.ValidateHttpsEndpoint(
            accountProfileEndpoint,
            "Endpoint de conta inválido.");
        endpoint = new Uri(accountProfileEndpoint, "entitlements");
    }

    public async Task<AccountEntitlementSnapshot> FetchAsync(
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
            return Unavailable();
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return Unavailable();
            }

            EntitlementResponseDto? body;
            try
            {
                body = await response.Content
                    .ReadFromJsonAsync<EntitlementResponseDto>(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return Unavailable();
            }

            if (body is null)
            {
                return Unavailable();
            }

            if (string.Equals(body.Tier, "free", StringComparison.Ordinal)
                && body.Entitlements is { Length: 0 }
                && body.ValidUntil is null)
            {
                return new AccountEntitlementSnapshot(AccountEntitlementTier.Free);
            }

            if (string.Equals(body.Tier, "pro", StringComparison.Ordinal)
                && body.Entitlements?.Contains(ProEntitlement, StringComparer.Ordinal) == true
                && DateTimeOffset.TryParse(
                    body.ValidUntil,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var validUntil)
                && validUntil > timeProvider.GetUtcNow())
            {
                return new AccountEntitlementSnapshot(
                    AccountEntitlementTier.Pro,
                    validUntil,
                    body.Entitlements.Contains(AiEntitlement, StringComparer.Ordinal));
            }

            return Unavailable();
        }
    }

    private static AccountEntitlementSnapshot Unavailable() =>
        new(AccountEntitlementTier.Unavailable);

    private sealed record EntitlementResponseDto(
        [property: JsonPropertyName("tier")] string? Tier,
        [property: JsonPropertyName("entitlements")] string[]? Entitlements,
        [property: JsonPropertyName("validUntil")] string? ValidUntil);
}
