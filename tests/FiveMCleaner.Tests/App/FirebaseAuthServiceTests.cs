using System.Net;
using System.Text;
using System.Text.Json;
using FiveMCleaner.App.Services;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class FirebaseAuthServiceTests
{
    [Fact]
    public void PasswordPolicy_UsesFirebaseConfiguredBoundsOnly()
    {
        Assert.False(AccountPasswordPolicy.IsValid(new string('a', 11)));
        Assert.True(AccountPasswordPolicy.IsValid(new string('a', 12)));
        Assert.True(AccountPasswordPolicy.IsValid(new string('a', 128)));
        Assert.False(AccountPasswordPolicy.IsValid(new string('a', 129)));
    }

    [Fact]
    public async Task RegisterAsync_UsesOfficialEndpointsAndRequiresEmailVerification()
    {
        var requests = new List<string>();
        using var service = CreateService(requests, request => request.RequestUri!.AbsolutePath switch
        {
            "/v1/accounts:signUp" => Json("""{"localId":"uid-1","email":"person@example.com","idToken":"id-1","refreshToken":"refresh-1","expiresIn":"3600"}"""),
            "/v1/accounts:lookup" => Json("""{"users":[{"localId":"uid-1","email":"person@example.com","emailVerified":false}]}"""),
            "/v1/accounts:sendOobCode" => Json("{}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var result = await service.RegisterAsync("person@example.com", "0123456789ab", keepSignedIn: false);

        Assert.True(result.Succeeded);
        Assert.Equal(AuthenticationState.EmailVerificationRequired, result.State);
        Assert.Equal("uid-1", result.User!.Uid);
        Assert.Contains("/v1/accounts:signUp", requests);
        Assert.Contains("/v1/accounts:sendOobCode", requests);
    }

    [Fact]
    public async Task SignInAsync_DoesNotRevealWhetherEmailExists()
    {
        using var service = CreateService([], _ => Json("""{"error":{"message":"EMAIL_NOT_FOUND"}}""", HttpStatusCode.BadRequest));

        var result = await service.SignInAsync("missing@example.com", "0123456789ab", keepSignedIn: false);

        Assert.False(result.Succeeded);
        Assert.DoesNotContain("e-mail", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("existe", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestoreSessionAsync_RefreshesTokenAndLoadsUid()
    {
        var path = Path.Combine(Path.GetTempPath(), $"firebase-{Guid.NewGuid():N}.session");
        var store = new SecureFirebaseSessionStore(path);
        await store.WriteAsync("refresh-1", CancellationToken.None);
        using var client = new HttpClient(new StubHandler(request => request.RequestUri!.Host == "securetoken.googleapis.com"
            ? Json("""{"user_id":"uid-1","id_token":"id-2","refresh_token":"refresh-2","expires_in":"3600"}""")
            : Json("""{"users":[{"localId":"uid-1","email":"person@example.com","emailVerified":true}]}""")));
        using var service = new FirebaseAuthService(client, "AIzaSyBrYcZtzioKnCc1-LmgCC2YI1R66SW4vdM", store);

        var result = await service.RestoreSessionAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(AuthenticationState.SignedIn, service.Current.State);
        Assert.Equal("uid-1", service.Current.User!.Uid);
        await service.LogoutAsync();
        Assert.False(File.Exists(path));
    }

    private static FirebaseAuthService CreateService(List<string> requests, Func<HttpRequestMessage, HttpResponseMessage> send)
    {
        var path = Path.Combine(Path.GetTempPath(), $"firebase-{Guid.NewGuid():N}.session");
        var client = new HttpClient(new StubHandler(request => { requests.Add(request.RequestUri!.AbsolutePath); return send(request); }));
        return new FirebaseAuthService(client, "AIzaSyBrYcZtzioKnCc1-LmgCC2YI1R66SW4vdM", new SecureFirebaseSessionStore(path));
    }

    private static HttpResponseMessage Json(string payload, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
}
