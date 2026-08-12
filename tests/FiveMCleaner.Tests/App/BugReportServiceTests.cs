using System.Globalization;
using System.Net;
using System.Net.Http;
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
        var handler = new RecordingHandler(HttpStatusCode.Accepted);
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);
        var submission = ValidSubmission() with
        {
            TechnicalSummary = "Windows 11; perfil médio",
            Email = "user@example.com",
            LogText = "2026-07-26 crash log excerpt"
        };

        var result = await service.SendAsync(submission, CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(TestEndpoint, handler.RequestUri);
        Assert.Contains("application/json", handler.ContentType, StringComparison.OrdinalIgnoreCase);

        using var body = JsonDocument.Parse(handler.Body);
        var root = body.RootElement;
        Assert.Equal(submission.ReportId.ToString("D"), root.GetProperty("reportId").GetString());
        Assert.Equal(submission.Category, root.GetProperty("category").GetString());
        Assert.Equal(submission.Summary, root.GetProperty("summary").GetString());
        Assert.Equal(submission.Description, root.GetProperty("description").GetString());
        Assert.Equal("Production", root.GetProperty("environment").GetString());
        Assert.Equal("user@example.com", root.GetProperty("email").GetString());
        Assert.Equal("2026-07-26 crash log excerpt", root.GetProperty("logText").GetString());
        Assert.False(root.TryGetProperty("attachment", out _));
        Assert.False(root.TryGetProperty("name", out _));
    }

    [Fact]
    public async Task SendAsync_RejectsAnInvalidEmailBeforeTransport()
    {
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);
        var invalid = ValidSubmission() with { Email = "not-an-email" };

        await Assert.ThrowsAsync<ArgumentException>(() => service.SendAsync(invalid, CancellationToken.None));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SendAsync_RejectsALogTextOverTheHundredKilobyteLimit()
    {
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);
        var invalid = ValidSubmission() with { LogText = new string('a', 101 * 1024) };

        await Assert.ThrowsAsync<ArgumentException>(() => service.SendAsync(invalid, CancellationToken.None));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SendAsync_MapsRateLimitWithoutRetry()
    {
        var handler = new RecordingHandler((HttpStatusCode)429);
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);

        var result = await service.SendAsync(ValidSubmission(), CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SendAsync_MapsAnyOtherNonSuccessStatusToAGenericHttpError()
    {
        var handler = new RecordingHandler(HttpStatusCode.InternalServerError);
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
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);
        var invalid = ValidSubmission() with { Description = "   " };

        await Assert.ThrowsAsync<ArgumentException>(() => service.SendAsync(invalid, CancellationToken.None));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public void Constructor_RejectsANonHttpsEndpoint()
    {
        using var httpClient = new HttpClient(new RecordingHandler());

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
        }, cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.False(result.Accepted);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }
}
