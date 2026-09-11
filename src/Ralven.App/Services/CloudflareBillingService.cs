using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ralven.App.Services;

public sealed record BillingOffer(string Key, int AmountCents, string Currency, int IntervalMonths);
public sealed record BillingSubscription(string State, DateTimeOffset? RenewsAt, DateTimeOffset? AccessUntil, bool CanCancel);
public sealed record BillingSnapshot(
    [property: JsonRequired] BillingOffer? Offer,
    [property: JsonRequired] bool CheckoutAvailable,
    [property: JsonRequired] BillingSubscription? Subscription);
public sealed record BillingResult<T>(T? Value, string? Error = null) where T : class;

/// <summary>Billing never grants access; the separate entitlement endpoint remains authoritative.</summary>
public sealed class CloudflareBillingService
{
    private const int MaximumResponseBytes = 16 * 1024;
    private static readonly HttpClient SharedClient = CloudflareTransportDefaults.CreateClient(TimeSpan.FromSeconds(25));
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly Uri endpoint;
    private readonly TimeSpan responseTimeout;

    public CloudflareBillingService(Uri accountProfileEndpoint) : this(SharedClient, accountProfileEndpoint) { }

    internal CloudflareBillingService(HttpClient httpClient, Uri accountProfileEndpoint, TimeSpan? responseTimeout = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.responseTimeout = responseTimeout ?? TimeSpan.FromSeconds(25);
        CloudflareTransportDefaults.ValidateHttpsEndpoint(accountProfileEndpoint, "Invalid account endpoint.");
        endpoint = new Uri(accountProfileEndpoint, "billing");
    }

    public Task<BillingResult<BillingSnapshot>> FetchAsync(string idToken, CancellationToken cancellationToken = default) =>
        ReadSnapshotAsync(idToken, HttpMethod.Get, "", null, cancellationToken);

    public Task<BillingResult<BillingSnapshot>> CancelAsync(string idToken, CancellationToken cancellationToken = default) =>
        ReadSnapshotAsync(idToken, HttpMethod.Post, "/cancel", new { }, cancellationToken);

    public async Task<BillingResult<Uri>> CheckoutAsync(string idToken, string offerKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(offerKey);
        var result = await SendAsync<CheckoutResponse>(idToken, HttpMethod.Post, "/checkout", new { offerKey }, cancellationToken).ConfigureAwait(false);
        return result.Value is { } checkout && TryValidateCheckoutUri(checkout.CheckoutUrl, out var uri)
            ? new(uri)
            : new(null, result.Error ?? "invalid-response");
    }

    internal static bool TryValidateCheckoutUri(string? value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps || !parsed.IsDefaultPort
            || parsed.UserInfo.Length != 0 || parsed.Fragment.Length != 0
            || parsed.IdnHost != "asaas.com" || parsed.AbsoluteUri.Length > 2048) return false;
        // Hosted subscription checkout only. A provider-hosted arbitrary redirect is not a checkout.
        if (parsed.AbsolutePath != "/checkoutSession/show") return false;
        var ids = parsed.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(part => Uri.UnescapeDataString(part[0]) == "id")
            .ToArray();
        if (ids.Length != 1 || ids[0].Length != 2
            || parsed.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Length != 1) return false;
        var id = Uri.UnescapeDataString(ids[0][1]);
        if (id.Length is < 1 or > 128
            || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-'))) return false;
        uri = parsed;
        return true;
    }

    private async Task<BillingResult<BillingSnapshot>> ReadSnapshotAsync(
        string token, HttpMethod method, string suffix, object? body, CancellationToken cancellationToken)
    {
        var result = await SendAsync<BillingSnapshot>(token, method, suffix, body, cancellationToken).ConfigureAwait(false);
        if (result.Value is not { } snapshot) return result;
        if (snapshot.Offer is { } offer
            && (string.IsNullOrWhiteSpace(offer.Key) || offer.Key.Length > 80 || offer.AmountCents is <= 0 or > 100000
                || offer.Currency != "BRL" || offer.IntervalMonths != 1)) return new(null, "invalid-response");
        if (snapshot.CheckoutAvailable && snapshot.Offer is null) return new(null, "invalid-response");
        if (snapshot.Subscription is { } subscription
            && subscription.State is not ("pending" or "authorized" or "paused" or "cancelled" or "canceled"
                or "active" or "creating" or "creation_unknown" or "expired" or "payment_pending")) return new(null, "invalid-response");
        return result;
    }

    private async Task<BillingResult<T>> SendAsync<T>(
        string token, HttpMethod method, string suffix, object? body, CancellationToken cancellationToken) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(responseTimeout);
        var requestToken = deadline.Token;
        using var request = new HttpRequestMessage(method, new Uri(endpoint.AbsoluteUri + suffix));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength > MaximumResponseBytes) return new(null, "invalid-response");
            await using var stream = await response.Content.ReadAsStreamAsync(requestToken).ConfigureAwait(false);
            using var content = new MemoryStream();
            var buffer = new byte[2048];
            int count;
            while ((count = await stream.ReadAsync(buffer, requestToken).ConfigureAwait(false)) > 0)
            {
                if (content.Length + count > MaximumResponseBytes) return new(null, "invalid-response");
                content.Write(buffer, 0, count);
            }
            if (!response.IsSuccessStatusCode)
            {
                var error = JsonSerializer.Deserialize<ErrorResponse>(content.ToArray(), JsonOptions)?.Error;
                // Only an allowlisted key can reach UI localization; never display remote free text.
                return new(null, error is "billing-disabled" or "billing-unavailable" or "billing-not-configured"
                    or "provider-temporarily-unavailable" or "billing-checkout-in-progress" or "billing-checkout-reconciliation-required"
                    or "billing-subscription-exists" or "billing-cancellation-unconfirmed"
                    or "billing-access-active"
                    or "billing-rate-limited" or "rate-limited" or "email-verification-required" or "subscription-exists" or "checkout-pending" or "checkout-unresolved"
                    or "checkout-reconciliation-required" or "offer-changed" or "invalid-offer"
                    or "billing-cancellation-required" or "cancellation-pending" ? error : "request-failed");
            }
            var result = JsonSerializer.Deserialize<T>(content.ToArray(), JsonOptions);
            return result is null ? new(null, "invalid-response") : new(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or OperationCanceledException)
        {
            return new(null, "request-failed");
        }
    }

    private sealed record CheckoutResponse(string? CheckoutUrl);
    private sealed record ErrorResponse(string? Error);
}
