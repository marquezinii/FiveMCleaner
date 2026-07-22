using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FiveMCleaner.Contracts;

namespace FiveMCleaner.App.Services;

public sealed record BugReportAttachment(
    string FileName,
    string ContentType,
    byte[] Content);

public sealed record BugReportSubmission
{
    public required Guid ReportId { get; init; }

    public required string Category { get; init; }

    public required string Summary { get; init; }

    public required string Description { get; init; }

    public required string AppVersion { get; init; }

    public required string Profile { get; init; }

    public string? TechnicalSummary { get; init; }

    public BugReportAttachment? Attachment { get; init; }
}

public sealed record BugReportSendResult(bool Accepted, string Message);

public interface IBugReportService
{
    Task<BugReportSendResult> SendAsync(
        BugReportSubmission submission,
        CancellationToken cancellationToken = default);
}

public sealed class FormSubmitBugReportService : IBugReportService
{
    public const string Endpoint =
        "https://formsubmit.co/ajax/482352ce22ed6e76ff37d235a5b667af";

    private static readonly HttpClient SharedClient = CreateClient();
    private readonly HttpClient httpClient;
    private readonly Uri endpoint;
    private readonly ILocalizationService localization;

    public FormSubmitBugReportService()
        : this(SharedClient, new Uri(Endpoint, UriKind.Absolute))
    {
    }

    internal FormSubmitBugReportService(HttpClient httpClient, Uri endpoint)
        : this(httpClient, endpoint, LocalizationService.Current)
    {
    }

    internal FormSubmitBugReportService(
        HttpClient httpClient,
        Uri endpoint,
        ILocalizationService localization)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.endpoint = ValidateEndpoint(endpoint);
        this.localization = localization ?? throw new ArgumentNullException(nameof(localization));
    }

    public async Task<BugReportSendResult> SendAsync(
        BugReportSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ValidateSubmission(submission);
        using var form = new MultipartFormDataContent();
        AddField(form, "_subject", $"[FiveMCleaner] {SingleLine(submission.Category)}: {SingleLine(submission.Summary)}");
        AddField(form, "_template", "table");
        AddField(form, "_captcha", "false");
        AddField(form, "_honey", string.Empty);
        AddField(form, "_url", ProductIdentity.RepositoryUrl);
        AddField(form, "ID do relato", submission.ReportId.ToString("D"));
        AddField(form, "Tipo do problema", submission.Category);
        AddField(form, "Resumo", submission.Summary.Trim());
        AddField(form, "Descrição e passos", submission.Description.Trim());
        AddField(form, "Versão do FiveMCleaner", submission.AppVersion);
        AddField(form, "Perfil selecionado", submission.Profile);
        AddField(form, "Identificação solicitada", "Não");
        AddField(form, "Versão do consentimento", "1");
        if (!string.IsNullOrWhiteSpace(submission.TechnicalSummary))
        {
            AddField(form, "Informações técnicas autorizadas", submission.TechnicalSummary);
        }

        if (submission.Attachment is not null)
        {
            var attachment = new ByteArrayContent(submission.Attachment.Content);
            attachment.Headers.ContentType = MediaTypeHeaderValue.Parse(
                submission.Attachment.ContentType);
            form.Add(attachment, "attachment", submission.Attachment.FileName);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = form
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd($"FiveMCleaner/{submission.AppVersion}");
        request.Headers.Referrer = new Uri(ProductIdentity.RepositoryUrl, UriKind.Absolute);
        request.Headers.TryAddWithoutValidation("Origin", "https://github.com");

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
        {
            return new BugReportSendResult(
                false,
                localization.GetString("BugReport.Service.ImageTooLarge"));
        }

        if ((int)response.StatusCode == 429)
        {
            return new BugReportSendResult(
                false,
                localization.GetString("BugReport.Service.RateLimited"));
        }

        if (!response.IsSuccessStatusCode)
        {
            return new BugReportSendResult(
                false,
                localization.Format("BugReport.Service.HttpError", (int)response.StatusCode));
        }

        var payload = await ReadBoundedResponseAsync(response, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!TryReadSuccess(document.RootElement))
            {
                var message = document.RootElement.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : null;
                return new BugReportSendResult(
                    false,
                    IsActivationRequired(message)
                        ? localization.GetString("BugReport.Service.ActivationRequired")
                        : localization.GetString("BugReport.Service.Unconfirmed"));
            }
        }
        catch (JsonException)
        {
            return new BugReportSendResult(
                false,
                localization.GetString("BugReport.Service.UnexpectedResponse"));
        }

        return new BugReportSendResult(
            true,
            localization.GetString("BugReport.Service.Accepted"));
    }

    public static string FormatForClipboard(BugReportSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        var builder = new StringBuilder();
        var localization = LocalizationService.Current;
        builder.AppendLine(localization.GetString("BugReport.Clipboard.Title"));
        builder.AppendLine(localization.Format("BugReport.Clipboard.Id", submission.ReportId));
        builder.AppendLine(localization.Format("BugReport.Clipboard.Category", submission.Category));
        builder.AppendLine(localization.Format("BugReport.Clipboard.Summary", submission.Summary.Trim()));
        builder.AppendLine(localization.Format("BugReport.Clipboard.Version", submission.AppVersion));
        builder.AppendLine(localization.Format("BugReport.Clipboard.Profile", submission.Profile));
        if (!string.IsNullOrWhiteSpace(submission.TechnicalSummary))
        {
            builder.AppendLine(localization.Format(
                "BugReport.Clipboard.Technical",
                submission.TechnicalSummary));
        }

        builder.AppendLine();
        builder.AppendLine(localization.GetString("BugReport.Clipboard.Description"));
        builder.AppendLine(submission.Description.Trim());
        return builder.ToString();
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    private static Uri ValidateEndpoint(Uri value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Scheme != Uri.UriSchemeHttps
            || !value.Host.Equals("formsubmit.co", StringComparison.OrdinalIgnoreCase)
            || !value.AbsolutePath.StartsWith("/ajax/", StringComparison.Ordinal))
        {
            throw new ArgumentException("Endpoint de relato inválido.", nameof(value));
        }

        return value;
    }

    private static void ValidateSubmission(BugReportSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        if (submission.ReportId == Guid.Empty)
        {
            throw new ArgumentException("O relato precisa de um identificador.", nameof(submission));
        }

        if (string.IsNullOrWhiteSpace(submission.Category)
            || submission.Category.Length > 60
            || submission.Category.IndexOfAny(['\r', '\n']) >= 0)
        {
            throw new ArgumentException("Escolha um tipo de problema válido.", nameof(submission));
        }

        if (string.IsNullOrWhiteSpace(submission.Summary)
            || submission.Summary.Trim().Length is < 5 or > 120)
        {
            throw new ArgumentException("O resumo deve ter entre 5 e 120 caracteres.", nameof(submission));
        }

        if (string.IsNullOrWhiteSpace(submission.Description)
            || submission.Description.Trim().Length is < 20 or > 8000)
        {
            throw new ArgumentException("A descrição deve ter entre 20 e 8.000 caracteres.", nameof(submission));
        }

        if (string.IsNullOrWhiteSpace(submission.AppVersion)
            || submission.AppVersion.Length > 32
            || submission.AppVersion.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-')))
        {
            throw new ArgumentException("A versão do app é inválida.", nameof(submission));
        }

        if (string.IsNullOrWhiteSpace(submission.Profile) || submission.Profile.Length > 32)
        {
            throw new ArgumentException("O perfil do relato é inválido.", nameof(submission));
        }

        if (submission.TechnicalSummary?.Length > 512)
        {
            throw new ArgumentException("As informações técnicas excedem o limite.", nameof(submission));
        }

        if (submission.Attachment is { } attachment
            && !IsValidSanitizedAttachment(attachment))
        {
            throw new ArgumentException("A imagem anexada não corresponde ao formato sanitizado.", nameof(submission));
        }
    }

    private static bool IsValidSanitizedAttachment(BugReportAttachment attachment)
    {
        return attachment.Content.Length is >= 8 and <= BugReportImageProcessor.MaximumAttachmentBytes
            && attachment.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(attachment.FileName).Equals(attachment.FileName, StringComparison.Ordinal)
            && attachment.FileName.StartsWith("captura-", StringComparison.Ordinal)
            && attachment.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            && attachment.Content.AsSpan(0, 8).SequenceEqual(
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
    }

    private static void AddField(MultipartFormDataContent form, string name, string value)
    {
        form.Add(new StringContent(value, Encoding.UTF8), name);
    }

    private static string SingleLine(string value)
    {
        return value.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private static bool TryReadSuccess(JsonElement root)
    {
        if (!root.TryGetProperty("success", out var success))
        {
            return false;
        }

        return success.ValueKind == JsonValueKind.True
            || success.ValueKind == JsonValueKind.String
            && bool.TryParse(success.GetString(), out var parsed)
            && parsed;
    }

    private static bool IsActivationRequired(string? message)
    {
        return !string.IsNullOrWhiteSpace(message)
            && (message.Contains("needs activation", StringComparison.OrdinalIgnoreCase)
                || message.Contains("activate form", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<byte[]> ReadBoundedResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        const int maximumBytes = 64 * 1024;
        if (response.Content.Headers.ContentLength is > maximumBytes)
        {
            throw new InvalidDataException("A resposta do serviço excedeu o limite esperado.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return destination.ToArray();
            }

            if (destination.Length + read > maximumBytes)
            {
                throw new InvalidDataException("A resposta do serviço excedeu o limite esperado.");
            }

            destination.Write(buffer, 0, read);
        }
    }
}
