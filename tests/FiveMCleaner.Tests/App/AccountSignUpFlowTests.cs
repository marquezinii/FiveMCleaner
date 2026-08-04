using System.Net;
using System.Text;
using System.Text.Json;
using FiveMCleaner.App.Services;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class AccountSignUpFlowTests
{
    [Theory]
    [InlineData("person@example.com")]
    [InlineData("person+tag@example.com")]
    [InlineData("joao@example.com.br")]
    [InlineData("Usuario@Example.COM")]
    [InlineData("  person@example.com  ")]
    public void IsValidEmail_AcceptsReasonableAddresses(string email) => Assert.True(AccountValidation.IsValidEmail(email));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("@example.com")]
    [InlineData("user@")]
    [InlineData("user@@example.com")]
    [InlineData("user@exa mple.com")]
    [InlineData("user@example..com")]
    [InlineData("user@.example.com")]
    [InlineData("user name@example.com")]
    [InlineData("a b@c.d")]
    public void IsValidEmail_RejectsMalformedAddresses(string? email) => Assert.False(AccountValidation.IsValidEmail(email));

    [Fact]
    public async Task RegisterAsync_HappyPath_CreatesAccountAndSendsVerificationEmail()
    {
        var requests = new List<string>();
        using var service = CreateService(requests, request => request.RequestUri!.AbsolutePath switch
        {
            "/v1/accounts:signUp" => Json("""{"localId":"uid-1","email":"person@example.com","idToken":"id-1","refreshToken":"refresh-1","expiresIn":"3600"}"""),
            "/v1/accounts:lookup" => Json("""{"users":[{"localId":"uid-1","email":"person@example.com","emailVerified":false}]}"""),
            "/v1/accounts:sendOobCode" => Json("{}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var result = await service.RegisterAsync("person@example.com", "abcdefghijkl", keepSignedIn: true);

        Assert.True(result.Succeeded);
        Assert.Equal(AuthenticationState.EmailVerificationRequired, result.State);
        Assert.Equal("uid-1", result.User!.Uid);
        Assert.Contains("/v1/accounts:signUp", requests);
        Assert.Contains("/v1/accounts:lookup", requests);
        Assert.Contains("/v1/accounts:sendOobCode", requests);
    }

    [Fact]
    public async Task RegisterAsync_KeepSignedInFalse_DoesNotPersistSession()
    {
        var path = TempSessionPath();
        using var service = CreateService([], request => request.RequestUri!.AbsolutePath switch
        {
            "/v1/accounts:signUp" => Json("""{"localId":"uid-1","email":"person@example.com","idToken":"id-1","refreshToken":"refresh-1","expiresIn":"3600"}"""),
            "/v1/accounts:lookup" => Json("""{"users":[{"localId":"uid-1","email":"person@example.com","emailVerified":false}]}"""),
            "/v1/accounts:sendOobCode" => Json("{}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        }, path);

        var result = await service.RegisterAsync("person@example.com", "abcdefghijkl", keepSignedIn: false);

        Assert.True(result.Succeeded);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("EMAIL_EXISTS", "Este e-mail já está em uso")]
    [InlineData("WEAK_PASSWORD", "A senha não atende à política de segurança")]
    [InlineData("INVALID_EMAIL", "Informe um endereço de e-mail válido")]
    [InlineData("TOO_MANY_ATTEMPTS_TRY_LATER", "Muitas tentativas")]
    public async Task RegisterAsync_SignUpRejected_ShowsMappedMessage(string firebaseCode, string expectedFragment)
    {
        using var service = CreateService([], _ => Json($"{{\"error\":{{\"message\":\"{firebaseCode}\"}}}}", HttpStatusCode.BadRequest));

        var result = await service.RegisterAsync("person@example.com", "abcdefghijkl", keepSignedIn: true);

        Assert.False(result.Succeeded);
        Assert.Equal(AuthenticationState.SigningIn, result.State);
        Assert.Contains(expectedFragment, result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RegisterAsync_NetworkFailure_ShowsFriendlyMessage()
    {
        using var service = CreateService([], _ => throw new HttpRequestException("boom"));

        var result = await service.RegisterAsync("person@example.com", "abcdefghijkl", keepSignedIn: true);

        Assert.False(result.Succeeded);
        Assert.Contains("Não foi possível conectar", result.Error!);
    }

    [Fact]
    public async Task RegisterAsync_HttpTimeout_MapsToFriendlyMessageInsteadOfCrashing()
    {
        using var service = CreateService([], _ => throw new TaskCanceledException("timeout", new TimeoutException()));

        var result = await service.RegisterAsync("person@example.com", "abcdefghijkl", keepSignedIn: true);

        Assert.False(result.Succeeded);
        Assert.Contains("Não foi possível conectar", result.Error!);
    }

    [Fact]
    public async Task RegisterAsync_VerificationEmailFailure_KeepsAccountVerifiedPending()
    {
        var requests = new List<string>();
        using var service = CreateService(requests, request => request.RequestUri!.AbsolutePath switch
        {
            "/v1/accounts:signUp" => Json("""{"localId":"uid-1","email":"person@example.com","idToken":"id-1","refreshToken":"refresh-1","expiresIn":"3600"}"""),
            "/v1/accounts:lookup" => Json("""{"users":[{"localId":"uid-1","email":"person@example.com","emailVerified":false}]}"""),
            "/v1/accounts:sendOobCode" => Json("""{"error":{"message":"TOO_MANY_ATTEMPTS_TRY_LATER"}}""", HttpStatusCode.TooManyRequests),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var result = await service.RegisterAsync("person@example.com", "abcdefghijkl", keepSignedIn: true);

        Assert.True(result.Succeeded);
        Assert.Equal(AuthenticationState.EmailVerificationRequired, result.State);
    }

    [Fact]
    public async Task RegisterAsync_LookupFailureAfterSignUp_TrapsAccountAndRetryHintsRecovery()
    {
        var signUps = 0;
        using var service = CreateService([], request => request.RequestUri!.AbsolutePath switch
        {
            "/v1/accounts:signUp" => signUps++ == 0
                ? Json("""{"localId":"uid-1","email":"person@example.com","idToken":"id-1","refreshToken":"refresh-1","expiresIn":"3600"}""")
                : Json("""{"error":{"message":"EMAIL_EXISTS"}}""", HttpStatusCode.BadRequest),
            "/v1/accounts:lookup" => throw new HttpRequestException("boom"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var first = await service.RegisterAsync("person@example.com", "abcdefghijkl", keepSignedIn: true);
        var retry = await service.RegisterAsync("person@example.com", "abcdefghijkl", keepSignedIn: true);

        Assert.False(first.Succeeded);
        Assert.Contains("Não foi possível conectar", first.Error!);
        Assert.False(retry.Succeeded);
        Assert.Contains("Esqueci minha senha", retry.Error!);
    }

    [Fact]
    public async Task SignInAsync_HttpTimeout_MapsToFriendlyMessageInsteadOfCrashing()
    {
        using var service = CreateService([], _ => throw new TaskCanceledException("timeout", new TimeoutException()));

        var result = await service.SignInAsync("person@example.com", "abcdefghijkl", keepSignedIn: false);

        Assert.False(result.Succeeded);
        Assert.Contains("Não foi possível conectar", result.Error!);
    }

    [Fact]
    public async Task RegisterAsync_CallerCancellation_StillPropagates()
    {
        using var service = CreateService([], _ => throw new OperationCanceledException());
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.RegisterAsync("person@example.com", "abcdefghijkl", keepSignedIn: true, source.Token));
    }

    [Fact]
    public async Task SessionStore_WriteFailure_IsBestEffortInsteadOfCrashing()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"firebase-session-{Guid.NewGuid():N}"));
        var blockedPath = Path.Combine(directory.FullName, "firebase.session");
        Directory.CreateDirectory(blockedPath);
        var store = new SecureFirebaseSessionStore(blockedPath);

        await store.WriteAsync("refresh-1", CancellationToken.None);

        Assert.True(Directory.Exists(blockedPath));
    }

    private static FirebaseAuthService CreateService(List<string> requests, Func<HttpRequestMessage, HttpResponseMessage> send, string? sessionPath = null)
    {
        var path = sessionPath ?? TempSessionPath();
        var client = new HttpClient(new StubHandler(request => { requests.Add(request.RequestUri!.AbsolutePath); return send(request); }));
        return new FirebaseAuthService(client, "AIzaSyBrYcZtzioKnCc1-LmgCC2YI1R66SW4vdM", new SecureFirebaseSessionStore(path));
    }

    private static string TempSessionPath() => Path.Combine(Path.GetTempPath(), $"firebase-{Guid.NewGuid():N}.session");

    private static HttpResponseMessage Json(string payload, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
}
