using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ralven.Contracts;

namespace Ralven.App.Services;

public sealed record RalvenAiActionContext(
    string Id,
    string Name,
    string ExpectedImpact,
    string Risk,
    bool Reversible);

public sealed record RalvenAiProfileContext(
    string Profile,
    IReadOnlyList<RalvenAiActionContext> Actions);

public sealed record RalvenAiPcContext(
    string Cpu,
    string Gpu,
    double TotalMemoryGiB,
    double AvailableMemoryGiB,
    int LogicalProcessors,
    double FreeDiskGiB,
    string OperatingSystem,
    string Architecture,
    int ReadinessScore,
    string PerformancePressure,
    string LocalRecommendedProfile,
    IReadOnlyList<RalvenAiProfileContext> Profiles);

public sealed record RalvenAiConversationTurn(string Role, string Text);

public sealed record RalvenAiReply(string Answer, OptimizationProfile? RecommendedProfile);

public enum RalvenAiError
{
    Unavailable,
    Unauthorized,
    ProRequired,
    AiAccessRequired,
    RateLimited,
    BudgetExhausted,
    RequestReplayed,
    InvalidResponse
}

public sealed class RalvenAiException(RalvenAiError error) : Exception(error.ToString())
{
    public RalvenAiError Error { get; } = error;
}

/// <summary>
/// Sends only the allowlisted diagnostic summary supplied by the view model.
/// The provider credential and entitlement decision remain in the Worker.
/// </summary>
public sealed class RalvenAiService
{
    private const int MaximumResponseBytes = 16 * 1024;
    private static readonly HttpClient SharedClient =
        CloudflareTransportDefaults.CreateClient(TimeSpan.FromSeconds(30));
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;
    private readonly Uri endpoint;

    public RalvenAiService(Uri accountProfileEndpoint)
        : this(SharedClient, accountProfileEndpoint)
    {
    }

    internal RalvenAiService(HttpClient httpClient, Uri accountProfileEndpoint)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        CloudflareTransportDefaults.ValidateHttpsEndpoint(
            accountProfileEndpoint,
            "Endpoint do Ralven AI inválido.");
        endpoint = new Uri(accountProfileEndpoint.GetLeftPart(UriPartial.Authority) + "/ai/message");
    }

    public async Task<RalvenAiReply> AskAsync(
        string idToken,
        string message,
        string language,
        RalvenAiPcContext context,
        IReadOnlyList<RalvenAiConversationTurn> history,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(history);

        var payload = new RequestDto(
            Guid.NewGuid().ToString("D"),
            ReportSanitizer.Sanitize(message.Trim()),
            language,
            context,
            history.TakeLast(6)
                .Select(turn => turn with { Text = ReportSanitizer.Sanitize(turn.Text) })
                .ToArray());

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                attempt == 0 && exception is (HttpRequestException or TaskCanceledException))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                throw new RalvenAiException(RalvenAiError.Unavailable);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new RalvenAiException(await MapErrorAsync(response, cancellationToken).ConfigureAwait(false));
                }

                ResponseDto? body;
                try
                {
                    await using var stream = await ReadBoundedAsync(response.Content, cancellationToken).ConfigureAwait(false);
                    body = await JsonSerializer.DeserializeAsync<ResponseDto>(stream, JsonOptions, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is JsonException or InvalidDataException)
                {
                    throw new RalvenAiException(RalvenAiError.InvalidResponse);
                }

                if (body is null || string.IsNullOrWhiteSpace(body.Answer) || body.Answer.Length > 2_000)
                {
                    throw new RalvenAiException(RalvenAiError.InvalidResponse);
                }

                OptimizationProfile? profile = body.RecommendedProfile?.ToLowerInvariant() switch
                {
                    "light" => OptimizationProfile.Light,
                    "balanced" => OptimizationProfile.Balanced,
                    "aggressive" => OptimizationProfile.Aggressive,
                    null or "none" => null,
                    _ => throw new RalvenAiException(RalvenAiError.InvalidResponse)
                };
                return new RalvenAiReply(body.Answer.Trim(), profile);
            }
        }
    }

    private static async Task<RalvenAiError> MapErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ErrorDto? body = null;
        try
        {
            await using var stream = await ReadBoundedAsync(response.Content, cancellationToken).ConfigureAwait(false);
            body = await JsonSerializer.DeserializeAsync<ErrorDto>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return RalvenAiError.Unavailable;
        }

        return (response.StatusCode, body?.Error) switch
        {
            (HttpStatusCode.Unauthorized, _) => RalvenAiError.Unauthorized,
            (HttpStatusCode.Forbidden, "pro-required") => RalvenAiError.ProRequired,
            (HttpStatusCode.Forbidden, "ai-access-required") => RalvenAiError.AiAccessRequired,
            (HttpStatusCode.Conflict, "request-in-progress" or "request-already-processed") =>
                RalvenAiError.RequestReplayed,
            (HttpStatusCode.TooManyRequests, "budget-exhausted") => RalvenAiError.BudgetExhausted,
            (HttpStatusCode.TooManyRequests, _) => RalvenAiError.RateLimited,
            _ => RalvenAiError.Unavailable
        };
    }

    private static async Task<MemoryStream> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumResponseBytes)
        {
            throw new InvalidDataException("Ralven AI response is too large.");
        }

        await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var destination = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                destination.Position = 0;
                return destination;
            }
            if (destination.Length + read > MaximumResponseBytes)
            {
                destination.Dispose();
                throw new InvalidDataException("Ralven AI response is too large.");
            }
            destination.Write(buffer, 0, read);
        }
    }

    private sealed record RequestDto(
        [property: JsonPropertyName("requestId")] string RequestId,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("language")] string Language,
        [property: JsonPropertyName("context")] RalvenAiPcContext Context,
        [property: JsonPropertyName("history")] IReadOnlyList<RalvenAiConversationTurn> History);

    private sealed record ResponseDto(
        [property: JsonPropertyName("answer")] string? Answer,
        [property: JsonPropertyName("recommendedProfile")] string? RecommendedProfile);

    private sealed record ErrorDto([property: JsonPropertyName("error")] string? Error);
}
