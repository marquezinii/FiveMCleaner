using System.Net;
using System.Net.Http;
using FiveMCleaner.App.Services;
using Xunit;

namespace FiveMCleaner.Tests.App;

/// <summary>
/// The interactive half of <see cref="GoogleOAuthClient"/> needs a real
/// browser and a real Google account, so it is verified manually. What is
/// covered here is the part that must hold without either: an unconfigured
/// build never opens a browser, never reaches the network, and never
/// pretends to have signed anyone in.
/// </summary>
public sealed class GoogleOAuthClientTests
{
    [Fact]
    public void IsConfigured_IsFalseWithoutAClientId()
    {
        Assert.False(new GoogleOAuthClient(null).IsConfigured);
        Assert.False(new GoogleOAuthClient(string.Empty).IsConfigured);
        Assert.False(new GoogleOAuthClient("   ").IsConfigured);
    }

    [Fact]
    public void IsConfigured_IsTrueWithAClientId()
    {
        Assert.True(new GoogleOAuthClient("1234-abc.apps.googleusercontent.com").IsConfigured);
    }

    [Fact]
    public async Task AuthenticateAsync_Unconfigured_FailsWithoutTouchingTheNetwork()
    {
        var called = false;
        using var client = new HttpClient(new ThrowingHandler(() => called = true));
        var oauth = new GoogleOAuthClient(client, clientId: null, clientSecret: null);

        var ticket = await oauth.AuthenticateAsync();

        Assert.Null(ticket.IdToken);
        Assert.False(string.IsNullOrWhiteSpace(ticket.Error));
        Assert.False(called);
    }

    private sealed class ThrowingHandler(Action onSend) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onSend();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
