using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FiveMCleaner.App.Services;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class CloudflareAccountProfileServiceTests
{
    private static readonly AccountProfileSubmission Submission = new()
    {
        Username = "joao_silva",
        FirstName = "João",
        LastName = "Silva",
    };

    [Fact]
    public async Task CreateAsync_Success_ReturnsCreated()
    {
        HttpRequestMessage? captured = null;
        var service = CreateService(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.Created);
        });

        var result = await service.CreateAsync("id-token-1", Submission);

        Assert.Equal(AccountProfileOutcome.Created, result.Outcome);
        Assert.Null(result.Message);
        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
        Assert.Equal("id-token-1", captured.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task CreateAsync_Conflict_ReturnsUsernameTakenWithAMessage()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.Conflict));

        var result = await service.CreateAsync("id-token-1", Submission);

        Assert.Equal(AccountProfileOutcome.UsernameTaken, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task CreateAsync_ServerError_ReturnsFailed()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await service.CreateAsync("id-token-1", Submission);

        Assert.Equal(AccountProfileOutcome.Failed, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task CreateAsync_NetworkFailure_ReturnsFailedInsteadOfThrowing()
    {
        var service = CreateService(_ => throw new HttpRequestException("network down"));

        var result = await service.CreateAsync("id-token-1", Submission);

        Assert.Equal(AccountProfileOutcome.Failed, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public void Constructor_RejectsNonHttpsEndpoint()
    {
        var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        Assert.Throws<ArgumentException>(() => new CloudflareAccountProfileService(client, new Uri("http://example.com/account/profile")));
    }

    private static CloudflareAccountProfileService CreateService(Func<HttpRequestMessage, HttpResponseMessage> send)
    {
        var client = new HttpClient(new StubHandler(send));
        return new CloudflareAccountProfileService(client, new Uri("https://example.com/account/profile"));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
}
