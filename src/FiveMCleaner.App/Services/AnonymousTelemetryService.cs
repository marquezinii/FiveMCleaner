using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Text;
using FiveMCleaner.Contracts;

namespace FiveMCleaner.App.Services;

/// <summary>
/// Evento técnico mínimo, sem texto livre, caminhos, identificadores de
/// máquina ou dados do usuário. O contrato deliberadamente não aceita campos
/// adicionais para evitar que telemetria se transforme em coleta de logs.
/// </summary>
public sealed record AnonymousTelemetryEvent(
    string EventName,
    TimeSpan ExecutionTime,
    string AppVersion,
    string? ErrorCategory = null);

public interface IAnonymousTelemetryService
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);

    Task TrackAsync(AnonymousTelemetryEvent telemetryEvent, CancellationToken cancellationToken = default);
}

public sealed class DisabledAnonymousTelemetryService : IAnonymousTelemetryService
{
    public static DisabledAnonymousTelemetryService Instance { get; } = new();

    private DisabledAnonymousTelemetryService()
    {
    }

    public bool IsEnabled => false;

    public void SetEnabled(bool enabled)
    {
    }

    public Task TrackAsync(AnonymousTelemetryEvent telemetryEvent, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

/// <summary>
/// Envia somente telemetria consentida para o mesmo endpoint HTTPS já usado
/// pelo formulário de bugs. Falhas de rede nunca interferem na otimização.
/// </summary>
public sealed class FormSubmitAnonymousTelemetryService : IAnonymousTelemetryService
{
    public const string Endpoint = FormSubmitBugReportService.Endpoint;
    private static readonly HttpClient SharedClient = CreateClient();
    private readonly HttpClient httpClient;
    private readonly Uri endpoint;
    private volatile bool enabled;

    public FormSubmitAnonymousTelemetryService()
        : this(SharedClient, new Uri(Endpoint, UriKind.Absolute))
    {
    }

    internal FormSubmitAnonymousTelemetryService(HttpClient httpClient, Uri endpoint)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.endpoint = ValidateEndpoint(endpoint);
    }

    public bool IsEnabled => enabled;

    public void SetEnabled(bool value) => enabled = value;

    public async Task TrackAsync(
        AnonymousTelemetryEvent telemetryEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(telemetryEvent);
        if (!enabled)
        {
            return;
        }

        ValidateEvent(telemetryEvent);
        using var form = new MultipartFormDataContent();
        AddField(form, "_subject", "[FiveMCleaner] Telemetria anônima");
        AddField(form, "_template", "table");
        AddField(form, "_captcha", "false");
        AddField(form, "Tipo", telemetryEvent.EventName);
        AddField(form, "Tempo de execução (ms)",
            Math.Clamp((long)telemetryEvent.ExecutionTime.TotalMilliseconds, 0, 86_400_000).ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddField(form, "Versão do FiveMCleaner", telemetryEvent.AppVersion);
        if (telemetryEvent.ErrorCategory is not null)
        {
            AddField(form, "Categoria de erro", telemetryEvent.ErrorCategory);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = form };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd($"FiveMCleaner/{telemetryEvent.AppVersion}");
        request.Headers.Referrer = new Uri(ProductIdentity.RepositoryUrl, UriKind.Absolute);
        request.Headers.TryAddWithoutValidation("Origin", "https://github.com");

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        _ = response.IsSuccessStatusCode;
    }

    public static string ClassifyException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            OperationCanceledException => "cancelled",
            TimeoutException => "timeout",
            UnauthorizedAccessException => "access-denied",
            IOException => "io",
            InvalidDataException => "invalid-data",
            _ => "unexpected"
        };
    }

    private static void ValidateEvent(AnonymousTelemetryEvent telemetryEvent)
    {
        if (telemetryEvent.EventName is not ("optimization-completed" or "optimization-failed" or "optimization-cancelled"))
        {
            throw new ArgumentException("Evento de telemetria não permitido.", nameof(telemetryEvent));
        }

        if (string.IsNullOrWhiteSpace(telemetryEvent.AppVersion)
            || telemetryEvent.AppVersion.Length > 32
            || telemetryEvent.AppVersion.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-')))
        {
            throw new ArgumentException("Versão de telemetria inválida.", nameof(telemetryEvent));
        }

        if (telemetryEvent.ErrorCategory is not null
            && telemetryEvent.ErrorCategory is not ("cancelled" or "timeout" or "access-denied" or "io" or "invalid-data" or "unexpected"))
        {
            throw new ArgumentException("Categoria de erro de telemetria não permitida.", nameof(telemetryEvent));
        }
    }

    private static Uri ValidateEndpoint(Uri value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Scheme != Uri.UriSchemeHttps
            || !value.Host.Equals("formsubmit.co", StringComparison.OrdinalIgnoreCase)
            || !value.AbsolutePath.StartsWith("/ajax/", StringComparison.Ordinal))
        {
            throw new ArgumentException("Endpoint de telemetria inválido.", nameof(value));
        }

        return value;
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
    }

    private static void AddField(MultipartFormDataContent form, string name, string value) =>
        form.Add(new StringContent(value, Encoding.UTF8), name);
}
