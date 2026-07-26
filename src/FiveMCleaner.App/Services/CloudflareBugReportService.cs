using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace FiveMCleaner.App.Services;

/// <summary>
/// Sends a bug report to the Cloudflare Worker's <c>/bugs</c> route (D1 for
/// the report itself, R2 for the optional screenshot attachment), replacing
/// <c>FormSubmitBugReportService</c> entirely. Validation mirrors what the
/// old service enforced client-side; the Worker re-validates everything
/// server-side regardless (see <c>infra/cloudflare-worker/src/bugReports/</c>).
/// </summary>
public sealed class CloudflareBugReportService : IBugReportService
{
    private const int MaxCategoryLength = 60;
    private const int MaxTechnicalSummaryLength = 512;

    private static readonly HttpClient SharedClient = CreateClient();
    private readonly HttpClient httpClient;
    private readonly Uri endpoint;
    private readonly string environment;
    private readonly ILocalizationService localization;

    public CloudflareBugReportService(Uri endpoint, string environment, ILocalizationService? localization = null)
        : this(SharedClient, endpoint, environment, localization)
    {
    }

    internal CloudflareBugReportService(
        HttpClient httpClient,
        Uri endpoint,
        string environment,
        ILocalizationService? localization = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.endpoint = ValidateEndpoint(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);
        this.environment = environment;
        this.localization = localization ?? LocalizationService.Current;
    }

    public async Task<BugReportSendResult> SendAsync(
        BugReportSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ValidateSubmission(submission);

        var payload = new
        {
            reportId = submission.ReportId.ToString("D"),
            category = submission.Category,
            summary = submission.Summary.Trim(),
            description = submission.Description.Trim(),
            appVersion = submission.AppVersion,
            profile = submission.Profile,
            technicalSummary = submission.TechnicalSummary,
            environment,
            attachment = submission.Attachment is { } attachment
                ? new
                {
                    fileName = attachment.FileName,
                    contentType = attachment.ContentType,
                    contentBase64 = Convert.ToBase64String(attachment.Content)
                }
                : null
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload)
        };

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
            return new BugReportSendResult(
                false,
                localization.GetString("BugReport.Service.Unconfirmed"));
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
            {
                return new BugReportSendResult(false, localization.GetString("BugReport.Service.ImageTooLarge"));
            }

            if ((int)response.StatusCode == 429)
            {
                return new BugReportSendResult(false, localization.GetString("BugReport.Service.RateLimited"));
            }

            if (!response.IsSuccessStatusCode)
            {
                return new BugReportSendResult(
                    false,
                    localization.Format("BugReport.Service.HttpError", (int)response.StatusCode));
            }

            return new BugReportSendResult(true, localization.GetString("BugReport.Service.Accepted"));
        }
    }

    private static void ValidateSubmission(BugReportSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        if (submission.ReportId == Guid.Empty)
        {
            throw new ArgumentException("O relato precisa de um identificador.", nameof(submission));
        }

        if (string.IsNullOrWhiteSpace(submission.Category)
            || submission.Category.Length > MaxCategoryLength
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

        if (submission.TechnicalSummary?.Length > MaxTechnicalSummaryLength)
        {
            throw new ArgumentException("As informações técnicas excedem o limite.", nameof(submission));
        }

        if (submission.Attachment is { } attachment && !IsValidSanitizedAttachment(attachment))
        {
            throw new ArgumentException("A imagem anexada não corresponde ao formato sanitizado.", nameof(submission));
        }
    }

    private static bool IsValidSanitizedAttachment(BugReportAttachment attachment)
    {
        return attachment.Content.Length is >= 8 and <= BugReportImageProcessor.MaximumAttachmentBytes
            && attachment.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
            && System.IO.Path.GetFileName(attachment.FileName).Equals(attachment.FileName, StringComparison.Ordinal)
            && attachment.FileName.StartsWith("captura-", StringComparison.Ordinal)
            && attachment.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            && attachment.Content.AsSpan(0, 8).SequenceEqual(
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
    }

    private static Uri ValidateEndpoint(Uri value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Endpoint de relato inválido.", nameof(value));
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
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }
}
