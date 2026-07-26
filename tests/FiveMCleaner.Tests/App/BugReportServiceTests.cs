using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FiveMCleaner.App.Services;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class CloudflareBugReportServiceTests
{
    private static readonly Uri TestEndpoint = new("https://fivemcleaner-telemetry.example.workers.dev/bugs", UriKind.Absolute);

    [Fact]
    public async Task SendAsync_PostsJsonWithTheAllowlistedFieldsAndTagsTheEnvironment()
    {
        var handler = new RecordingHttpMessageHandler { StatusCode = HttpStatusCode.Accepted };
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);
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
        Assert.Contains("application/json", handler.ContentType, StringComparison.OrdinalIgnoreCase);

        using var body = JsonDocument.Parse(handler.RequestBody);
        var root = body.RootElement;
        Assert.Equal(submission.ReportId.ToString("D"), root.GetProperty("reportId").GetString());
        Assert.Equal(submission.Category, root.GetProperty("category").GetString());
        Assert.Equal(submission.Summary, root.GetProperty("summary").GetString());
        Assert.Equal(submission.Description, root.GetProperty("description").GetString());
        Assert.Equal("Production", root.GetProperty("environment").GetString());
        Assert.Equal("captura-test.png", root.GetProperty("attachment").GetProperty("fileName").GetString());
        Assert.False(root.TryGetProperty("email", out _));
        Assert.False(root.TryGetProperty("name", out _));
    }

    [Fact]
    public async Task SendAsync_MapsRateLimitWithoutRetry()
    {
        var handler = new RecordingHttpMessageHandler { StatusCode = (HttpStatusCode)429 };
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);

        var result = await service.SendAsync(ValidSubmission(), CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SendAsync_MapsTooLargeAttachment()
    {
        var handler = new RecordingHttpMessageHandler { StatusCode = HttpStatusCode.RequestEntityTooLarge };
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);

        var result = await service.SendAsync(ValidSubmission(), CancellationToken.None);

        Assert.False(result.Accepted);
    }

    [Fact]
    public async Task SendAsync_NetworkFailure_ReturnsAFailureResultInsteadOfThrowing()
    {
        var handler = new ThrowingHandler();
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);

        var result = await service.SendAsync(ValidSubmission(), CancellationToken.None);

        Assert.False(result.Accepted);
    }

    [Fact]
    public async Task SendAsync_RejectsMissingDescriptionBeforeTransport()
    {
        var handler = new RecordingHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);
        var invalid = ValidSubmission() with { Description = "   " };

        await Assert.ThrowsAsync<ArgumentException>(() => service.SendAsync(invalid, CancellationToken.None));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public void Constructor_RejectsANonHttpsEndpoint()
    {
        using var httpClient = new HttpClient(new RecordingHttpMessageHandler());

        Assert.Throws<ArgumentException>(() =>
            new CloudflareBugReportService(httpClient, new Uri("http://insecure.example.com"), "Production"));
    }

    private static CloudflareBugReportService CreateService(HttpClient httpClient) =>
        new(httpClient, TestEndpoint, "Production", new LocalizationService(CultureInfo.GetCultureInfo("pt-BR")));

    private static BugReportSubmission ValidSubmission() => new()
    {
        ReportId = Guid.NewGuid(),
        Category = "Falha na otimização",
        Summary = "O preset não terminou",
        Description = "Ao aplicar o perfil médio, a operação parou antes da conclusão.",
        AppVersion = "1.0.0",
        Profile = "Médio"
    };

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.Accepted;
        public int CallCount { get; private set; }
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string ContentType { get; private set; } = string.Empty;
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Method = request.Method;
            RequestUri = request.RequestUri;
            ContentType = request.Content?.Headers.ContentType?.ToString() ?? string.Empty;
            if (request.Content is not null)
            {
                RequestBody = Encoding.UTF8.GetString(await request.Content.ReadAsByteArrayAsync(cancellationToken));
            }

            return new HttpResponseMessage(StatusCode) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("simulated network failure");
    }
}

public sealed class DisabledBugReportServiceTests
{
    [Fact]
    public async Task SendAsync_AlwaysReturnsAnHonestFailure()
    {
        var service = new DisabledBugReportService(new LocalizationService(CultureInfo.GetCultureInfo("pt-BR")));

        var result = await service.SendAsync(new BugReportSubmission
        {
            ReportId = Guid.NewGuid(),
            Category = "x",
            Summary = "x",
            Description = "x",
            AppVersion = "1.0.0",
            Profile = "Médio"
        });

        Assert.False(result.Accepted);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }
}
