using System.Net;
using System.Text;
using FiveMCleaner.App.Services;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class AnonymousTelemetryServiceTests
{
    private static readonly Uri TestEndpoint = new(
        "https://formsubmit.co/ajax/test-telemetry-token",
        UriKind.Absolute);

    [Fact]
    public async Task TrackAsync_DoesNothingUntilTheUserOptsIn()
    {
        var handler = new RecordingHttpMessageHandler();
        using var client = new HttpClient(handler);
        var service = new FormSubmitAnonymousTelemetryService(client, TestEndpoint);

        await service.TrackAsync(ValidEvent());

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task TrackAsync_SendsOnlyTheMinimalAllowlistedPayload()
    {
        var handler = new RecordingHttpMessageHandler();
        using var client = new HttpClient(handler);
        var service = new FormSubmitAnonymousTelemetryService(client, TestEndpoint);
        service.SetEnabled(true);

        await service.TrackAsync(ValidEvent() with { ErrorCategory = "timeout" });

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(TestEndpoint, handler.RequestUri);
        Assert.Contains("optimization-failed", handler.Body, StringComparison.Ordinal);
        Assert.Contains("18342", handler.Body, StringComparison.Ordinal);
        Assert.Contains("1.0.2", handler.Body, StringComparison.Ordinal);
        Assert.Contains("timeout", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("path", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("file", handler.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TrackAsync_RejectsFreeTextErrorCategoriesBeforeTransport()
    {
        var handler = new RecordingHttpMessageHandler();
        using var client = new HttpClient(handler);
        var service = new FormSubmitAnonymousTelemetryService(client, TestEndpoint);
        service.SetEnabled(true);

        await Assert.ThrowsAsync<ArgumentException>(() => service.TrackAsync(
            ValidEvent() with { ErrorCategory = "C:\\Users\\Felipe\\secret.txt" }));

        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData(typeof(TimeoutException), "timeout")]
    [InlineData(typeof(UnauthorizedAccessException), "access-denied")]
    [InlineData(typeof(IOException), "io")]
    [InlineData(typeof(InvalidDataException), "invalid-data")]
    public void ClassifyException_UsesOnlyFixedCategories(Type exceptionType, string expected)
    {
        var exception = Assert.IsAssignableFrom<Exception>(Activator.CreateInstance(exceptionType)!);

        Assert.Equal(expected, FormSubmitAnonymousTelemetryService.ClassifyException(exception));
    }

    private static AnonymousTelemetryEvent ValidEvent() => new(
        "optimization-failed",
        TimeSpan.FromMilliseconds(18_342),
        "1.0.2");

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Method = request.Method;
            RequestUri = request.RequestUri;
            Body = request.Content is null
                ? string.Empty
                : Encoding.UTF8.GetString(await request.Content.ReadAsByteArrayAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true}", Encoding.UTF8, "application/json")
            };
        }
    }
}
