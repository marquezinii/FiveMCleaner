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
    public async Task FetchAsync_Success_ReturnsFoundWithParsedFields()
    {
        HttpRequestMessage? captured = null;
        var service = CreateService(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"username\":\"joao_silva\",\"firstName\":\"João\",\"lastName\":\"Silva\"}",
                    Encoding.UTF8,
                    "application/json"),
            };
        });

        var result = await service.FetchAsync("id-token-1");

        Assert.Equal(AccountProfileFetchOutcome.Found, result.Outcome);
        Assert.Equal("joao_silva", result.Username);
        Assert.Equal("João", result.FirstName);
        Assert.Equal("Silva", result.LastName);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
        Assert.Equal("id-token-1", captured.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task FetchAsync_NotFound_ReturnsNotFound()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await service.FetchAsync("id-token-1");

        Assert.Equal(AccountProfileFetchOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task FetchAsync_ServerError_ReturnsFailed()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await service.FetchAsync("id-token-1");

        Assert.Equal(AccountProfileFetchOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task FetchAsync_NetworkFailure_ReturnsFailedInsteadOfThrowing()
    {
        var service = CreateService(_ => throw new HttpRequestException("network down"));

        var result = await service.FetchAsync("id-token-1");

        Assert.Equal(AccountProfileFetchOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task CheckUsernameAsync_Available_TargetsTheProbeRouteOnTheSameOrigin()
    {
        HttpRequestMessage? captured = null;
        var service = CreateService(request =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """{"available":true}""");
        });

        var result = await service.CheckUsernameAsync("joao_silva");

        Assert.Equal(UsernameAvailability.Available, result);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("https://example.com/account/username-available", captured.RequestUri!.GetLeftPart(UriPartial.Path));
        Assert.Equal("?u=joao_silva", captured.RequestUri!.Query);
    }

    [Fact]
    public async Task CheckUsernameAsync_Taken_ReturnsTaken()
    {
        var service = CreateService(_ => Json(HttpStatusCode.OK, """{"available":false}"""));

        Assert.Equal(UsernameAvailability.Taken, await service.CheckUsernameAsync("joao_silva"));
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("1joao")]
    [InlineData("joao silva")]
    public async Task CheckUsernameAsync_LocallyInvalid_NeverReachesTheNetwork(string username)
    {
        var called = false;
        var service = CreateService(_ =>
        {
            called = true;
            return Json(HttpStatusCode.OK, """{"available":true}""");
        });

        Assert.Equal(UsernameAvailability.Invalid, await service.CheckUsernameAsync(username));
        Assert.False(called);
    }

    [Fact]
    public async Task CheckUsernameAsync_ServerRejectsTheName_ReturnsInvalid()
    {
        var service = CreateService(_ => Json(HttpStatusCode.BadRequest, """{"error":"invalid-username"}"""));

        Assert.Equal(UsernameAvailability.Invalid, await service.CheckUsernameAsync("joao_silva"));
    }

    /// <summary>
    /// Rate limiting, a server error, an unreadable body or no connection at
    /// all must never be reported as "available" — that would tell the user a
    /// name is free moments before registration fails on it.
    /// </summary>
    [Fact]
    public async Task CheckUsernameAsync_RateLimited_ReturnsUnknownNotAvailable()
    {
        var service = CreateService(_ => Json(HttpStatusCode.TooManyRequests, """{"error":"rate-limited"}"""));

        Assert.Equal(UsernameAvailability.Unknown, await service.CheckUsernameAsync("joao_silva"));
    }

    [Fact]
    public async Task CheckUsernameAsync_ServerError_ReturnsUnknown()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        Assert.Equal(UsernameAvailability.Unknown, await service.CheckUsernameAsync("joao_silva"));
    }

    [Fact]
    public async Task CheckUsernameAsync_UnreadableBody_ReturnsUnknown()
    {
        var service = CreateService(_ => Json(HttpStatusCode.OK, "not json at all"));

        Assert.Equal(UsernameAvailability.Unknown, await service.CheckUsernameAsync("joao_silva"));
    }

    [Fact]
    public async Task CheckUsernameAsync_NetworkFailure_ReturnsUnknownInsteadOfThrowing()
    {
        var service = CreateService(_ => throw new HttpRequestException("network down"));

        Assert.Equal(UsernameAvailability.Unknown, await service.CheckUsernameAsync("joao_silva"));
    }

    [Fact]
    public async Task DisabledAccountProfileService_NeverClaimsANameIsAvailable()
    {
        Assert.Equal(UsernameAvailability.Unknown, await new DisabledAccountProfileService().CheckUsernameAsync("joao_silva"));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

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
