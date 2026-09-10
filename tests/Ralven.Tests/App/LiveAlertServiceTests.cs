using System.Net;
using System.Net.Http;
using System.Text;
using Ralven.App.Services;
using Xunit;

namespace Ralven.Tests.App;

public sealed class CloudflareLiveAlertServiceTests
{
    private static readonly Uri TestEndpoint = new("https://ralven-telemetry.example.workers.dev/live-alert", UriKind.Absolute);

    [Fact]
    public async Task GetCurrentAsync_ReturnsSnapshot_WhenActiveWithAValidMessage()
    {
        var handler = JsonResponse("""{"id":"2026-08-17T12:00:00.000Z","message":"Entre no Discord","active":true}""");
        using var httpClient = new HttpClient(handler);
        var service = new CloudflareLiveAlertService(httpClient, TestEndpoint);

        var result = await service.GetCurrentAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("2026-08-17T12:00:00.000Z", result!.Id);
        Assert.Equal("Entre no Discord", result.Message);
        Assert.True(result.Active);
        Assert.Equal(LiveAlertSeverity.Important, result.Severity);
    }

    [Fact]
    public async Task GetCurrentAsync_MapsTheCriticalSeverity()
    {
        var handler = JsonResponse("""{"id":"x","message":"Atualize o Ralven","active":true,"severity":"critical"}""");
        using var httpClient = new HttpClient(handler);
        var service = new CloudflareLiveAlertService(httpClient, TestEndpoint);

        var result = await service.GetCurrentAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(LiveAlertSeverity.Critical, result!.Severity);
    }

    [Fact]
    public async Task GetCurrentAsync_ReturnsInactiveSnapshot_WhenActiveIsFalse()
    {
        var handler = JsonResponse("""{"id":"2026-08-17T12:00:00.000Z","message":"texto antigo","active":false}""");
        using var httpClient = new HttpClient(handler);
        var service = new CloudflareLiveAlertService(httpClient, TestEndpoint);

        var result = await service.GetCurrentAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result!.Active);
        Assert.Null(result.Id);
        Assert.Equal(string.Empty, result.Message);
    }

    [Fact]
    public async Task GetCurrentAsync_ReturnsNull_WhenActiveTrueButMessageIsEmpty()
    {
        var handler = JsonResponse("""{"id":"x","message":"   ","active":true}""");
        using var httpClient = new HttpClient(handler);
        var service = new CloudflareLiveAlertService(httpClient, TestEndpoint);

        Assert.Null(await service.GetCurrentAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetCurrentAsync_ReturnsNull_OnNonSuccessStatus()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var httpClient = new HttpClient(handler);
        var service = new CloudflareLiveAlertService(httpClient, TestEndpoint);

        Assert.Null(await service.GetCurrentAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetCurrentAsync_ReturnsNull_OnMalformedJson()
    {
        var handler = JsonResponse("not json");
        using var httpClient = new HttpClient(handler);
        var service = new CloudflareLiveAlertService(httpClient, TestEndpoint);

        Assert.Null(await service.GetCurrentAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetCurrentAsync_ReturnsNull_OnNetworkFailure()
    {
        var handler = new ThrowingHandler();
        using var httpClient = new HttpClient(handler);
        var service = new CloudflareLiveAlertService(httpClient, TestEndpoint);

        Assert.Null(await service.GetCurrentAsync(CancellationToken.None));
    }

    [Fact]
    public void Constructor_RejectsANonHttpsEndpoint()
    {
        using var httpClient = new HttpClient(new ThrowingHandler());

        Assert.Throws<ArgumentException>(() =>
            new CloudflareLiveAlertService(httpClient, new Uri("http://insecure.example.com/live-alert")));
    }

    private static StubHandler JsonResponse(string json) => new(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    });
}
