using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Ralven.App.Services;
using Xunit;

namespace Ralven.Tests.App;

public sealed class CloudflareAccountSecurityServiceTests
{
    private static readonly CancellationToken Cancellation =
        global::Xunit.TestContext.Current.CancellationToken;

    [Fact]
    public async Task GenerateRecoveryCodesAsync_UsesBearerAndAcceptsOnlyBoundedUniqueCodes()
    {
        HttpRequestMessage? captured = null;
        var service = Create(request =>
        {
            captured = request;
            return Json(HttpStatusCode.Created, """{"recoveryCodes":["23456-789AB","CDEFG-HJKLM"]}""");
        });

        var result = await service.GenerateRecoveryCodesAsync("id-token", "enrollment-1", Cancellation);

        Assert.True(result.Succeeded);
        Assert.Equal(["23456-789AB", "CDEFG-HJKLM"], result.RecoveryCodes);
        Assert.Equal("https://example.com/account/mfa/recovery-codes", captured!.RequestUri!.AbsoluteUri);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "id-token"), captured.Headers.Authorization);
    }

    [Fact]
    public async Task GenerateRecoveryCodesAsync_RejectsDuplicateOrOversizedResponse()
    {
        var duplicate = await Create(_ => Json(
            HttpStatusCode.OK,
            """{"recoveryCodes":["23456-789AB","23456-789AB"]}"""))
            .GenerateRecoveryCodesAsync("token", "enrollment", Cancellation);
        var oversized = await Create(_ => Json(
            HttpStatusCode.OK,
            $$"""{"recoveryCodes":["{{new string('A', 129)}}"]}"""))
            .GenerateRecoveryCodesAsync("token", "enrollment", Cancellation);

        Assert.Equal(RecoveryCodesOutcome.Failed, duplicate.Outcome);
        Assert.Equal(RecoveryCodesOutcome.Failed, oversized.Outcome);
        Assert.Empty(duplicate.RecoveryCodes);
        Assert.Empty(oversized.RecoveryCodes);
    }

    [Fact]
    public async Task GenerateRecoveryCodesAsync_MapsReauthenticationAndRateLimit()
    {
        var reauth = await Create(_ => Json(
            HttpStatusCode.Forbidden,
            """{"error":"reauthentication-required"}"""))
            .GenerateRecoveryCodesAsync("token", "enrollment", Cancellation);
        var limited = await Create(_ => Json(
            HttpStatusCode.TooManyRequests,
            """{"error":"account-rate-limited"}"""))
            .GenerateRecoveryCodesAsync("token", "enrollment", Cancellation);

        Assert.Equal(RecoveryCodesOutcome.ReauthenticationRequired, reauth.Outcome);
        Assert.Equal(RecoveryCodesOutcome.RateLimited, limited.Outcome);
    }

    [Fact]
    public async Task GenerateRecoveryCodesAsync_PreservesMissingEnrollmentState()
    {
        var result = await Create(_ => Json(
            HttpStatusCode.NotFound,
            """{"error":"mfa-enrollment-not-found"}"""))
            .GenerateRecoveryCodesAsync("token", "stale-enrollment", Cancellation);

        Assert.Equal(RecoveryCodesOutcome.EnrollmentNotFound, result.Outcome);
        Assert.Equal("mfa-enrollment-not-found", result.ErrorCode);
    }

    [Fact]
    public async Task RecoverAsync_SendsNoBearerAndRequiresExplicitSuccess()
    {
        HttpRequestMessage? captured = null;
        var service = Create(request =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """{"success":true}""");
        });

        var result = await service.RecoverAsync("pending-credential", "enrollment-1", "23456-789AB", Cancellation);

        Assert.True(result.Succeeded);
        Assert.Equal("https://example.com/account/mfa/recover", captured!.RequestUri!.AbsoluteUri);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Null(captured.Headers.Authorization);
    }

    [Theory]
    [InlineData("invalid-recovery-code")]
    [InlineData("recovery-code-used")]
    [InlineData("invalid-recovery-proof")]
    public async Task RecoverAsync_DoesNotDistinguishInvalidFromUsedCode(string error)
    {
        var result = await Create(_ => Json(HttpStatusCode.Unauthorized, $$"""{"error":"{{error}}"}"""))
            .RecoverAsync("pending-credential", "enrollment", "23456-789AB", Cancellation);

        Assert.Equal(AccountRecoveryOutcome.InvalidOrUsedCode, result.Outcome);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RecoverAsync_PreservesConcurrentRecoveryState()
    {
        var result = await Create(_ => Json(
            HttpStatusCode.Conflict,
            """{"error":"recovery-in-progress"}"""))
            .RecoverAsync("pending-credential", "enrollment", "23456-789AB", Cancellation);

        Assert.Equal(AccountRecoveryOutcome.InProgress, result.Outcome);
        Assert.Equal("recovery-in-progress", result.ErrorCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "mfa-enrollment-not-found", RecoveryCodesDeletionOutcome.InvalidInput)]
    [InlineData(HttpStatusCode.Unauthorized, "reauthentication-required", RecoveryCodesDeletionOutcome.ReauthenticationRequired)]
    [InlineData(HttpStatusCode.TooManyRequests, "account-rate-limited", RecoveryCodesDeletionOutcome.RateLimited)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "account-security-unavailable", RecoveryCodesDeletionOutcome.Unavailable)]
    public async Task DeleteRecoveryCodesAsync_PreservesServerFailureReason(
        HttpStatusCode status,
        string error,
        RecoveryCodesDeletionOutcome expected)
    {
        var result = await Create(_ => Json(status, $$"""{"error":"{{error}}"}"""))
            .DeleteRecoveryCodesAsync("id-token", "enrollment-1", Cancellation);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(error, result.ErrorCode);
    }

    [Fact]
    public async Task GenerateRecoveryCodesAsync_RejectsOversizedResponse()
    {
        var responseBody = $$"""{"recoveryCodes":["{{new string('A', (16 * 1024) + 1)}}"]}""";

        var result = await Create(_ => Json(HttpStatusCode.OK, responseBody))
            .GenerateRecoveryCodesAsync("token", "enrollment", Cancellation);

        Assert.Equal(RecoveryCodesOutcome.Failed, result.Outcome);
        Assert.Empty(result.RecoveryCodes);
    }

    [Fact]
    public async Task DeleteRecoveryCodesAsync_IsAuthenticatedAndIdempotent()
    {
        HttpRequestMessage? captured = null;
        var result = await Create(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }).DeleteRecoveryCodesAsync("id-token", "enrollment-1", Cancellation);

        Assert.True(result.Succeeded);
        Assert.Equal(HttpMethod.Delete, captured!.Method);
        Assert.Equal("https://example.com/account/mfa/recovery-codes", captured.RequestUri!.AbsoluteUri);
        Assert.Equal("id-token", captured.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task InvalidInputs_NeverReachTheNetwork()
    {
        var called = false;
        var service = Create(_ =>
        {
            called = true;
            return Json(HttpStatusCode.OK, "{}");
        });

        var codes = await service.GenerateRecoveryCodesAsync(" ", "enrollment", Cancellation);
        var recovery = await service.RecoverAsync("pending", "enrollment", "\r", Cancellation);
        var deletion = await service.DeleteRecoveryCodesAsync("token", new string('x', 257), Cancellation);

        Assert.Equal(RecoveryCodesOutcome.InvalidInput, codes.Outcome);
        Assert.Equal(AccountRecoveryOutcome.InvalidInput, recovery.Outcome);
        Assert.Equal(RecoveryCodesDeletionOutcome.InvalidInput, deletion.Outcome);
        Assert.False(called);
    }

    [Fact]
    public async Task TransportFailureIsUnavailableAndCallerCancellationIsPropagated()
    {
        var unavailable = await Create(_ => throw new HttpRequestException("fixture failure"))
            .GenerateRecoveryCodesAsync("token", "enrollment", Cancellation);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Equal(RecoveryCodesOutcome.Unavailable, unavailable.Outcome);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Create(_ => throw new OperationCanceledException(cancelled.Token))
                .GenerateRecoveryCodesAsync("token", "enrollment", cancelled.Token));
    }

    [Fact]
    public void Constructor_RejectsNonHttpsOrUnexpectedPath()
    {
        Assert.Throws<ArgumentException>(() => new CloudflareAccountSecurityService(
            new Uri("http://example.com/account/profile")));
        Assert.Throws<ArgumentException>(() => new CloudflareAccountSecurityService(
            new Uri("https://example.com/not-profile")));
    }

    private static CloudflareAccountSecurityService Create(Func<HttpRequestMessage, HttpResponseMessage> send) =>
        new(new HttpClient(new StubHandler(send)), new Uri("https://example.com/account/profile"));

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
