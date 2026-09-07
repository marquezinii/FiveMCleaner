using System.Net;
using System.Text;
using System.Text.Json;
using Ralven.App.Services;
using Ralven.Contracts;
using Xunit;

namespace Ralven.Tests.App;

public sealed class RalvenAiServiceTests
{
    [Fact]
    public async Task AskAsync_UsesAuthenticatedWorkerAndAcceptsOnlyKnownProfile()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var client = new HttpClient(new StubHandler(request =>
        {
            captured = request;
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.OK, """{"answer":"Review the balanced plan.","recommendedProfile":"balanced"}""");
        }));
        var service = new RalvenAiService(client, new Uri("https://example.com/account/profile"));

        var reply = await service.AskAsync(
            "id-token",
            @"Check C:\Users\Alice\secret.txt",
            "en-US",
            Context(),
            [],
            global::Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(OptimizationProfile.Balanced, reply.RecommendedProfile);
        Assert.Equal("https://example.com/ai/message", captured!.RequestUri!.AbsoluteUri);
        Assert.Equal("application/json", captured.Content!.Headers.ContentType!.ToString());
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
        Assert.Equal("id-token", captured.Headers.Authorization.Parameter);
        using var json = JsonDocument.Parse(body!);
        Assert.Equal(
            @"Check %USERPROFILE%\secret.txt",
            json.RootElement.GetProperty("message").GetString());
        Assert.False(body!.Contains("Alice", StringComparison.Ordinal));

        var invalid = new RalvenAiService(
            new HttpClient(new StubHandler(_ => Json(
                HttpStatusCode.OK,
                """{"answer":"No.","recommendedProfile":"ultra"}"""))),
            new Uri("https://example.com/account/profile"));
        var exception = await Assert.ThrowsAsync<RalvenAiException>(() => invalid.AskAsync(
            "id-token", "question", "en-US", Context(), [],
            global::Xunit.TestContext.Current.CancellationToken));
        Assert.Equal(RalvenAiError.InvalidResponse, exception.Error);
    }

    [Fact]
    public async Task AskAsync_MapsMonthlyBudgetWithoutExposingRemoteDetails()
    {
        var service = new RalvenAiService(
            new HttpClient(new StubHandler(_ => Json(
                HttpStatusCode.TooManyRequests,
                """{"error":"budget-exhausted","detail":"provider secret"}"""))),
            new Uri("https://example.com/account/profile"));

        var exception = await Assert.ThrowsAsync<RalvenAiException>(() => service.AskAsync(
            "id-token", "question", "pt-BR", Context(), [],
            global::Xunit.TestContext.Current.CancellationToken));

        Assert.Equal(RalvenAiError.BudgetExhausted, exception.Error);
        Assert.DoesNotContain("provider secret", exception.Message, StringComparison.Ordinal);
    }

    private static RalvenAiPcContext Context() => new(
        "CPU", "GPU", 16, 8, 8, 100, "Windows", "x64", 80, "low", "balanced",
        [
            new("light", []),
            new("balanced", []),
            new("aggressive", [])
        ]);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
