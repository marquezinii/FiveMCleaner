using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Ralven.App.Services;
using Xunit;

namespace Ralven.Tests.App;

public sealed class CloudflareAccountProfileServiceTests
{
    private static readonly AccountProfileSubmission Submission = new()
    {
        Username = "joao_silva",
        FirstName = "João",
        LastName = "Silva",
        TermsVersion = AccountTerms.CurrentVersion,
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

        var result = await service.CreateAsync("id-token-1", Submission, cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(AccountProfileOutcome.Created, result.Outcome);
        Assert.Null(result.Message);
        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
        Assert.Equal("id-token-1", captured.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task CreateAsync_UsernameConflict_ReturnsUsernameTakenWithAMessage()
    {
        var service = CreateService(_ => Json(HttpStatusCode.Conflict, """{"error":"username-taken"}"""));

        var result = await service.CreateAsync("id-token-1", Submission, cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(AccountProfileOutcome.UsernameTaken, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task CreateAsync_UidConflict_DoesNotClaimTheUsernameIsTaken()
    {
        var service = CreateService(_ => Json(HttpStatusCode.Conflict, """{"error":"uid-taken"}"""));

        var result = await service.CreateAsync("id-token-1", Submission, cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(AccountProfileOutcome.UidTaken, result.Outcome);
        Assert.Null(result.Message);
    }

    [Fact]
    public async Task CreateAsync_UnknownConflict_ReturnsFailed()
    {
        var service = CreateService(_ => Json(HttpStatusCode.Conflict, """{"error":"unknown"}"""));

        var result = await service.CreateAsync("id-token-1", Submission, cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(AccountProfileOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task CreateAsync_ServerError_ReturnsFailed()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await service.CreateAsync("id-token-1", Submission, cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(AccountProfileOutcome.Failed, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task CreateAsync_NetworkFailure_ReturnsFailedInsteadOfThrowing()
    {
        var service = CreateService(_ => throw new HttpRequestException("network down"));

        var result = await service.CreateAsync("id-token-1", Submission, cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(AccountProfileOutcome.Failed, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("pt-BR")]
    [InlineData("es")]
    public async Task CreateAsync_NetworkFailure_UsesTheSelectedLanguage(string cultureName)
    {
        var localization = new LocalizationService(System.Globalization.CultureInfo.GetCultureInfo(cultureName));
        var client = new HttpClient(new StubHandler(_ => throw new HttpRequestException("network down")));
        var service = new CloudflareAccountProfileService(
            client,
            new Uri("https://example.com/account/profile"),
            localization);

        var result = await service.CreateAsync("id-token-1", Submission, cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(localization["Account.Profile.ConnectionFailed"], result.Message);
        Assert.NotEqual("Account.Profile.ConnectionFailed", result.Message);
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

        var result = await service.FetchAsync("id-token-1", cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

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

        var result = await service.FetchAsync("id-token-1", cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(AccountProfileFetchOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task FetchAsync_ServerError_ReturnsFailed()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await service.FetchAsync("id-token-1", cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(AccountProfileFetchOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task FetchAsync_NetworkFailure_ReturnsFailedInsteadOfThrowing()
    {
        var service = CreateService(_ => throw new HttpRequestException("network down"));

        var result = await service.FetchAsync("id-token-1", cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(AccountProfileFetchOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task DeleteAsync_UsesTheAuthenticatedAccountRoute()
    {
        HttpRequestMessage? captured = null;
        var service = CreateService(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var result = await service.DeleteAsync("id-token-1", cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(AccountProfileDeletionOutcome.Deleted, result.Outcome);
        Assert.Equal(HttpMethod.Delete, captured!.Method);
        Assert.Equal("https://example.com/account", captured.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
        Assert.Equal("id-token-1", captured.Headers.Authorization!.Parameter);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "billing-cancellation-required", AccountProfileDeletionOutcome.BillingCancellationRequired)]
    [InlineData(HttpStatusCode.Unauthorized, "reauthentication-required", AccountProfileDeletionOutcome.ReauthenticationRequired)]
    [InlineData(HttpStatusCode.TooManyRequests, "account-rate-limited", AccountProfileDeletionOutcome.RateLimited)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "account-deletion-unavailable", AccountProfileDeletionOutcome.Unavailable)]
    public async Task DeleteAsync_PreservesServerFailureReason(
        HttpStatusCode status,
        string error,
        AccountProfileDeletionOutcome expected)
    {
        var service = CreateService(_ => Json(status, $$"""{"error":"{{error}}"}"""));

        var result = await service.DeleteAsync(
            "id-token-1",
            cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(error, result.ErrorCode);
    }

    [Fact]
    public async Task DeleteAsync_PropagatesCallerCancellationAndMapsTransportFailure()
    {
        var unavailable = await CreateService(_ => throw new HttpRequestException("fixture failure"))
            .DeleteAsync("id-token-1", global::Xunit.TestContext.Current.CancellationToken);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Equal(AccountProfileDeletionOutcome.Unavailable, unavailable.Outcome);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService(_ => throw new OperationCanceledException(cancelled.Token))
                .DeleteAsync("id-token-1", cancelled.Token));
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

        var result = await service.CheckUsernameAsync("joao_silva", cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(UsernameAvailability.Available, result);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("https://example.com/account/username-available", captured.RequestUri!.GetLeftPart(UriPartial.Path));
        Assert.Equal("?u=joao_silva", captured.RequestUri!.Query);
    }

    [Fact]
    public async Task CheckUsernameAsync_Taken_ReturnsTaken()
    {
        var service = CreateService(_ => Json(HttpStatusCode.OK, """{"available":false}"""));

        Assert.Equal(UsernameAvailability.Taken, await service.CheckUsernameAsync("joao_silva", cancellationToken: global::Xunit.TestContext.Current.CancellationToken));
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

        Assert.Equal(UsernameAvailability.Invalid, await service.CheckUsernameAsync(username, cancellationToken: global::Xunit.TestContext.Current.CancellationToken));
        Assert.False(called);
    }

    [Fact]
    public async Task CheckUsernameAsync_ServerRejectsTheName_ReturnsInvalid()
    {
        var service = CreateService(_ => Json(HttpStatusCode.BadRequest, """{"error":"invalid-username"}"""));

        Assert.Equal(UsernameAvailability.Invalid, await service.CheckUsernameAsync("joao_silva", cancellationToken: global::Xunit.TestContext.Current.CancellationToken));
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

        Assert.Equal(UsernameAvailability.Unknown, await service.CheckUsernameAsync("joao_silva", cancellationToken: global::Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CheckUsernameAsync_ServerError_ReturnsUnknown()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        Assert.Equal(UsernameAvailability.Unknown, await service.CheckUsernameAsync("joao_silva", cancellationToken: global::Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CheckUsernameAsync_UnreadableBody_ReturnsUnknown()
    {
        var service = CreateService(_ => Json(HttpStatusCode.OK, "not json at all"));

        Assert.Equal(UsernameAvailability.Unknown, await service.CheckUsernameAsync("joao_silva", cancellationToken: global::Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CheckUsernameAsync_NetworkFailure_ReturnsUnknownInsteadOfThrowing()
    {
        var service = CreateService(_ => throw new HttpRequestException("network down"));

        Assert.Equal(UsernameAvailability.Unknown, await service.CheckUsernameAsync("joao_silva", cancellationToken: global::Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisabledAccountProfileService_NeverClaimsANameIsAvailable()
    {
        Assert.Equal(UsernameAvailability.Unknown, await new DisabledAccountProfileService().CheckUsernameAsync("joao_silva", cancellationToken: global::Xunit.TestContext.Current.CancellationToken));
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
}
