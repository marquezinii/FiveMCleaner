using System.Net;
using System.Text;
using Ralven.App.Services;
using Xunit;

namespace Ralven.Tests.App;

public sealed class CloudflareAccountEntitlementServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse(
        "2026-08-29T12:00:00.000Z",
        System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task FetchAsync_Free_UsesAuthenticatedEntitlementRoute()
    {
        HttpRequestMessage? captured = null;
        var service = CreateService(request =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """{"tier":"free","entitlements":[],"validUntil":null}""");
        });

        var result = await service.FetchAsync(
            "id-token-1",
            global::Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(AccountEntitlementTier.Free, result.Tier);
        Assert.Null(result.ValidUntil);
        Assert.False(result.HasRalvenAi);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("https://example.com/account/entitlements", captured.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
        Assert.Equal("id-token-1", captured.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task FetchAsync_Pro_RequiresEntitlementAndParsesValidity()
    {
        var service = CreateService(_ => Json(
            HttpStatusCode.OK,
            """{"tier":"pro","entitlements":["ralven_pro","ralven_ai"],"validUntil":"2026-09-30T12:00:00.000Z"}"""));

        var result = await service.FetchAsync(
            "id-token-1",
            global::Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(AccountEntitlementTier.Pro, result.Tier);
        Assert.Equal(
            DateTimeOffset.Parse("2026-09-30T12:00:00.000Z", System.Globalization.CultureInfo.InvariantCulture),
            result.ValidUntil);
        Assert.True(result.HasRalvenAi);
    }

    [Fact]
    public async Task FetchAsync_ProDoesNotImplyRalvenAiAccess()
    {
        var service = CreateService(_ => Json(
            HttpStatusCode.OK,
            """{"tier":"pro","entitlements":["ralven_pro"],"validUntil":"2026-09-30T12:00:00.000Z"}"""));

        var result = await service.FetchAsync(
            "id-token-1",
            global::Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(AccountEntitlementTier.Pro, result.Tier);
        Assert.False(result.HasRalvenAi);
    }

    [Fact]
    public async Task FetchAsync_MalformedOrFailedResponses_NeverGrantAccess()
    {
        var responses = new Func<HttpRequestMessage, HttpResponseMessage>[]
        {
            _ => Json(HttpStatusCode.OK, """{"tier":"pro","entitlements":[],"validUntil":"2026-09-30T12:00:00.000Z"}"""),
            _ => Json(HttpStatusCode.OK, """{"tier":"pro","entitlements":["ralven_pro"],"validUntil":"2026-08-29T11:59:59.999Z"}"""),
            _ => Json(HttpStatusCode.OK, "not-json"),
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            _ => throw new HttpRequestException("network down"),
        };

        foreach (var response in responses)
        {
            var result = await CreateService(response).FetchAsync(
                "id-token-1",
                global::Xunit.TestContext.Current.CancellationToken);

            Assert.Equal(AccountEntitlementTier.Unavailable, result.Tier);
            Assert.Null(result.ValidUntil);
            Assert.False(result.HasRalvenAi);
        }
    }

    [Fact]
    public void Constructor_RejectsNonHttpsEndpoint()
    {
        var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        Assert.Throws<ArgumentException>(() => new CloudflareAccountEntitlementService(
            client,
            new Uri("http://example.com/account/profile")));
    }

    private static CloudflareAccountEntitlementService CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> send)
    {
        var client = new HttpClient(new StubHandler(send));
        return new CloudflareAccountEntitlementService(
            client,
            new Uri("https://example.com/account/profile"),
            new FixedTimeProvider(Now));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
