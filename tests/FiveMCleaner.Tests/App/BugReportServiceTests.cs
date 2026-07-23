using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using FiveMCleaner.App.Services;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class BugReportServiceTests
{
    private static readonly Uri TestEndpoint = new(
        "https://formsubmit.co/ajax/test-endpoint-token",
        UriKind.Absolute);

    [Fact]
    public async Task SendAsync_UsesMultipartContractAndAcceptsConfirmedJsonWithoutNetwork()
    {
        var handler = new RecordingHttpMessageHandler
        {
            StatusCode = HttpStatusCode.OK,
            ResponseBody = "{\"success\":true,\"message\":\"received\"}"
        };
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient, TestEndpoint);
        var submission = ValidSubmission() with
        {
            TechnicalSummary = "Windows 11; perfil médio",
            Attachment = new BugReportAttachment(
                "captura-test.png",
                "image/png",
                [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])
        };

        var result = await service.SendAsync(submission, CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(TestEndpoint, handler.RequestUri);
        Assert.Contains("application/json", handler.Accept, StringComparison.Ordinal);
        Assert.Contains("FiveMCleaner/1.0.0", handler.UserAgent, StringComparison.Ordinal);
        Assert.Equal("https://github.com/marquezinii/FiveMCleaner", handler.Referrer);
        Assert.Equal("https://github.com", handler.Origin);
        Assert.StartsWith("multipart/form-data", handler.ContentType, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("_captcha", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("ID do relato", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains(submission.ReportId.ToString("D"), handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("captura-test.png", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("image/png", handler.RequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("name=email", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("name=\"email\"", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("name=name", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("name=\"name\"", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_MapsRateLimitWithoutRetryOrRealNetwork()
    {
        var handler = new RecordingHttpMessageHandler
        {
            StatusCode = HttpStatusCode.TooManyRequests,
            ResponseBody = "{\"success\":false}"
        };
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient, TestEndpoint);

        var result = await service.SendAsync(ValidSubmission(), CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Contains("Aguarde", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SendAsync_PropagatesOfflineFailureAfterSingleAttempt()
    {
        var handler = new RecordingHttpMessageHandler
        {
            Exception = new HttpRequestException("offline")
        };
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient, TestEndpoint);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.SendAsync(ValidSubmission(), CancellationToken.None));

        Assert.Equal("offline", exception.Message);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SendAsync_TranslatesActivationRequirementToPortuguese()
    {
        var handler = new RecordingHttpMessageHandler
        {
            ResponseBody = "{\"success\":\"false\",\"message\":\"This form needs Activation. Activate Form.\"}"
        };
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient, TestEndpoint);

        var result = await service.SendAsync(ValidSubmission(), CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Contains("ativação", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Activate Form", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_RejectsMissingDescriptionBeforeTransport()
    {
        var handler = new RecordingHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient, TestEndpoint);
        var invalid = ValidSubmission() with { Description = "   " };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SendAsync(invalid, CancellationToken.None));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public void FormatForClipboard_ContainsUsefulFieldsWithoutInventingIdentity()
    {
        var submission = ValidSubmission();

        var text = FormSubmitBugReportService.FormatForClipboard(submission);

        Assert.Contains(submission.ReportId.ToString("D"), text, StringComparison.Ordinal);
        Assert.Contains(submission.Category, text, StringComparison.Ordinal);
        Assert.Contains(submission.Summary, text, StringComparison.Ordinal);
        Assert.Contains(submission.Description, text, StringComparison.Ordinal);
        Assert.DoesNotContain("E-mail:", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Nome:", text, StringComparison.OrdinalIgnoreCase);
    }

    private static FormSubmitBugReportService CreateService(
        HttpClient httpClient,
        Uri endpoint)
    {
        var constructor = typeof(FormSubmitBugReportService).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(HttpClient), typeof(Uri), typeof(ILocalizationService)],
            modifiers: null);
        Assert.NotNull(constructor);
        return Assert.IsType<FormSubmitBugReportService>(
            constructor.Invoke([
                httpClient,
                endpoint,
                new LocalizationService(CultureInfo.GetCultureInfo("pt-BR"))
            ]));
    }

    private static BugReportSubmission ValidSubmission()
    {
        return new BugReportSubmission
        {
            ReportId = Guid.NewGuid(),
            Category = "Falha na otimização",
            Summary = "O preset não terminou",
            Description = "Ao aplicar o perfil médio, a operação parou antes da conclusão.",
            AppVersion = "1.0.0",
            Profile = "Médio"
        };
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;

        public string ResponseBody { get; init; } = "{\"success\":true}";

        public Exception? Exception { get; init; }

        public int CallCount { get; private set; }

        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string Accept { get; private set; } = string.Empty;

        public string UserAgent { get; private set; } = string.Empty;

        public string Referrer { get; private set; } = string.Empty;

        public string Origin { get; private set; } = string.Empty;

        public string ContentType { get; private set; } = string.Empty;

        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Method = request.Method;
            RequestUri = request.RequestUri;
            Accept = request.Headers.Accept.ToString();
            UserAgent = request.Headers.UserAgent.ToString();
            Referrer = request.Headers.Referrer?.ToString().TrimEnd('/') ?? string.Empty;
            Origin = request.Headers.TryGetValues("Origin", out var origins)
                ? origins.Single()
                : string.Empty;
            ContentType = request.Content?.Headers.ContentType?.ToString() ?? string.Empty;
            if (request.Content is not null)
            {
                var body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                RequestBody = Encoding.UTF8.GetString(body);
            }

            if (Exception is not null)
            {
                throw Exception;
            }

            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
