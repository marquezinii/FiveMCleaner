using System.Net;
using System.Text;
using System.Text.Json;
using FiveMCleaner.App.Services;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class FirebaseAuthServiceTests
{
    [Fact]
    public void PasswordPolicy_RequiresOnlyMinimumLength_NoCharacterClasses()
    {
        Assert.False(AccountPasswordPolicy.IsValid(string.Empty));
        Assert.False(AccountPasswordPolicy.IsValid(null));
        Assert.False(AccountPasswordPolicy.IsValid(new string('a', 11)));
        Assert.True(AccountPasswordPolicy.IsValid(new string('a', 12)));
        Assert.True(AccountPasswordPolicy.IsValid(new string('1', 12)));
        Assert.True(AccountPasswordPolicy.IsValid(new string('A', 12)));
        Assert.True(AccountPasswordPolicy.IsValid(new string('*', 12)));
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
            "/v1/accounts:lookup" => Json("""{"users":[{"localId":"uid-1","email":"person@example.com","emailVerified":false,"providerUserInfo":[{"providerId":"password"}]}]}"""),
            "/v1/accounts:sendOobCode" => Json("{}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var result = await service.RegisterAsync("person@example.com", "0123456789ab", keepSignedIn: false, cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(AuthenticationState.EmailVerificationRequired, result.State);
        Assert.Equal("uid-1", result.User!.Uid);
        Assert.True(result.User.HasPassword);
        Assert.Contains("/v1/accounts:signUp", requests);
        Assert.Contains("/v1/accounts:sendOobCode", requests);
    }

    [Fact]
    public async Task SignInAsync_DoesNotRevealWhetherEmailExists()
    {
        using var service = CreateService([], _ => Json("""{"error":{"message":"EMAIL_NOT_FOUND"}}""", HttpStatusCode.BadRequest));

        var result = await service.SignInAsync("missing@example.com", "0123456789ab", keepSignedIn: false, cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

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
        using var service = new FirebaseAuthService(client, "test-firebase-api-key-1234567890", store);

        var result = await service.RestoreSessionAsync(cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(AuthenticationState.SignedIn, service.Current.State);
        Assert.Equal("uid-1", service.Current.User!.Uid);
        await service.LogoutAsync(cancellationToken: global::Xunit.TestContext.Current.CancellationToken);
        Assert.False(File.Exists(path));
    }

    /// <summary>
    /// Regression guard: a rejected/expired refresh token used to make
    /// RefreshAsync clear the session by calling LogoutCoreAsync while still
    /// holding its own session lock, which then tried to re-acquire that same
    /// (non-reentrant) semaphore and hung forever. This must complete
    /// promptly, sign the user out, and leave the lock usable for the next call.
    /// </summary>
    [Fact]
    public async Task RestoreSessionAsync_RejectedRefreshToken_SignsOutInsteadOfHanging()
    {
        var path = Path.Combine(Path.GetTempPath(), $"firebase-{Guid.NewGuid():N}.session");
        var store = new SecureFirebaseSessionStore(path);
        await store.WriteAsync("refresh-1", CancellationToken.None);
        using var client = new HttpClient(new StubHandler(_ => Json("""{"error":"invalid_grant"}""", HttpStatusCode.BadRequest)));
        using var service = new FirebaseAuthService(client, "test-firebase-api-key-1234567890", store);

        var restoreTask = service.RestoreSessionAsync(cancellationToken: global::Xunit.TestContext.Current.CancellationToken);
        var completed = await Task.WhenAny(restoreTask, Task.Delay(TimeSpan.FromSeconds(10), global::Xunit.TestContext.Current.CancellationToken));

        Assert.Same(restoreTask, completed);
        var result = await restoreTask;
        Assert.False(result.Succeeded);
        Assert.Equal(AuthenticationState.SignedOut, result.State);
        Assert.False(File.Exists(path));

        // The lock must be free again for a follow-up call instead of stuck
        // (permanently held, or over-released into a broken state).
        await store.WriteAsync("refresh-2", CancellationToken.None);
        var secondTask = service.RestoreSessionAsync(cancellationToken: global::Xunit.TestContext.Current.CancellationToken);
        var secondCompleted = await Task.WhenAny(secondTask, Task.Delay(TimeSpan.FromSeconds(10), global::Xunit.TestContext.Current.CancellationToken));
        Assert.Same(secondTask, secondCompleted);
        Assert.False((await secondTask).Succeeded);
    }

    [Fact]
    public async Task SignInWithGoogleAsync_PostsTheAssertionToSignInWithIdpAndSkipsEmailVerification()
    {
        var requests = new List<string>();
        string? body = null;
        using var service = CreateService(requests, request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/accounts:signInWithIdp")
            {
                body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return Json("""{"localId":"uid-g","email":"person@gmail.com","idToken":"id-g","refreshToken":"refresh-g","expiresIn":"3600","firstName":"João","lastName":"Silva","isNewUser":true}""");
            }
            return Json("""{"users":[{"localId":"uid-g","email":"person@gmail.com","emailVerified":true,"providerUserInfo":[{"providerId":"google.com"}]}]}""");
        });

        var federated = await service.SignInWithGoogleAsync("google-id-token", keepSignedIn: false, cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.True(federated.Result.Succeeded);
        // Google has already verified the address, so the account goes
        // straight to SignedIn instead of the e-mail confirmation step.
        Assert.Equal(AuthenticationState.SignedIn, federated.Result.State);
        Assert.Equal("uid-g", federated.Result.User!.Uid);
        Assert.False(federated.Result.User.HasPassword);
        Assert.True(federated.IsNewUser);
        Assert.Equal("João", federated.FirstName);
        Assert.Equal("Silva", federated.LastName);
        Assert.Contains("/v1/accounts:signInWithIdp", requests);
        Assert.Contains("google.com", body!);
        Assert.Contains("google-id-token", body!);
    }

    [Fact]
    public async Task SignInWithGoogleAsync_ReturningAccount_IsNotFlaggedAsNew()
    {
        using var service = CreateService([], request => request.RequestUri!.AbsolutePath == "/v1/accounts:signInWithIdp"
            ? Json("""{"localId":"uid-g","email":"person@gmail.com","idToken":"id-g","refreshToken":"refresh-g","expiresIn":"3600","isNewUser":false}""")
            : Json("""{"users":[{"localId":"uid-g","email":"person@gmail.com","emailVerified":true}]}"""));

        var federated = await service.SignInWithGoogleAsync("google-id-token", keepSignedIn: false, cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.True(federated.Result.Succeeded);
        Assert.False(federated.IsNewUser);
    }

    [Fact]
    public async Task SignInWithGoogleAsync_ProviderRejection_FailsWithoutLeakingDetails()
    {
        using var service = CreateService([], _ => Json("""{"error":{"message":"INVALID_IDP_RESPONSE"}}""", HttpStatusCode.BadRequest));

        var federated = await service.SignInWithGoogleAsync("bad-token", keepSignedIn: false, cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.False(federated.Result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(federated.Result.Error));
        Assert.False(federated.IsNewUser);
        Assert.Null(federated.FirstName);
    }

    [Fact]
    public async Task CreatePasswordAsync_ReauthenticatesSameGoogleAccountAndLinksPasswordProvider()
    {
        var passwordCreated = false;
        string? requestBody = null;
        using var service = CreateService([], request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/accounts:update")
            {
                requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            }
            return request.RequestUri.AbsolutePath switch
            {
                "/v1/accounts:signInWithIdp" => Json("""{"localId":"uid-g","email":"person@gmail.com","idToken":"id-g","refreshToken":"refresh-g","expiresIn":"3600","isNewUser":false}"""),
                "/v1/accounts:lookup" => Json(passwordCreated
                    ? """{"users":[{"localId":"uid-g","email":"person@gmail.com","emailVerified":true,"providerUserInfo":[{"providerId":"google.com"},{"providerId":"password"}]}]}"""
                    : """{"users":[{"localId":"uid-g","email":"person@gmail.com","emailVerified":true,"providerUserInfo":[{"providerId":"google.com"}]}]}"""),
                "/v1/accounts:update" => SetPasswordCreated(),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        });

        HttpResponseMessage SetPasswordCreated()
        {
            passwordCreated = true;
            return Json("""{"localId":"uid-g","email":"person@gmail.com","idToken":"id-p","refreshToken":"refresh-p","expiresIn":"3600"}""");
        }

        var signedIn = await service.SignInWithGoogleAsync("google-token", keepSignedIn: false, global::Xunit.TestContext.Current.CancellationToken);
        var reauthenticated = await service.ReauthenticateWithGoogleAsync("google-token", global::Xunit.TestContext.Current.CancellationToken);
        var created = await service.CreatePasswordAsync("0123456789ab", global::Xunit.TestContext.Current.CancellationToken);

        Assert.True(signedIn.Result.Succeeded);
        Assert.True(reauthenticated.Succeeded);
        Assert.True(created.Succeeded);
        Assert.True(created.User!.HasPassword);
        Assert.NotNull(requestBody);
        Assert.Contains("person@gmail.com", requestBody, StringComparison.Ordinal);
        Assert.Contains("0123456789ab", requestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReauthenticateWithGoogleAsync_RejectsDifferentGoogleAccountWithoutReplacingSession()
    {
        using var service = CreateService([], request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/accounts:lookup")
            {
                return Json("""{"users":[{"localId":"uid-g","email":"person@gmail.com","emailVerified":true,"providerUserInfo":[{"providerId":"google.com"}]}]}""");
            }

            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return body.Contains("other-token", StringComparison.Ordinal)
                ? Json("""{"localId":"uid-other","email":"other@gmail.com","idToken":"id-other","refreshToken":"refresh-other","expiresIn":"3600"}""")
                : Json("""{"localId":"uid-g","email":"person@gmail.com","idToken":"id-g","refreshToken":"refresh-g","expiresIn":"3600"}""");
        });

        await service.SignInWithGoogleAsync("google-token", keepSignedIn: false, global::Xunit.TestContext.Current.CancellationToken);
        var result = await service.ReauthenticateWithGoogleAsync("other-token", global::Xunit.TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("GOOGLE_ACCOUNT_MISMATCH", result.Error);
        Assert.Equal(AuthenticationState.SignedIn, service.Current.State);
        Assert.Equal("uid-g", service.Current.User!.Uid);
    }

    [Fact]
    public async Task ChangePasswordAsync_RejectsWrongCurrentPasswordWithoutLosingSignedInSession()
    {
        var requests = new List<string>();
        using var service = CreateService(requests, request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/accounts:lookup")
            {
                return Json("""{"users":[{"localId":"uid-p","email":"person@example.com","emailVerified":true,"providerUserInfo":[{"providerId":"password"}]}]}""");
            }

            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return body.Contains("wrong-password", StringComparison.Ordinal)
                ? Json("""{"error":{"message":"INVALID_LOGIN_CREDENTIALS"}}""", HttpStatusCode.BadRequest)
                : Json("""{"localId":"uid-p","email":"person@example.com","idToken":"id-p","refreshToken":"refresh-p","expiresIn":"3600"}""");
        });

        await service.SignInAsync("person@example.com", "current-password", keepSignedIn: false, global::Xunit.TestContext.Current.CancellationToken);
        var result = await service.ChangePasswordAsync("wrong-password", "0123456789ab", global::Xunit.TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("CURRENT_PASSWORD_INVALID", result.Error);
        Assert.Equal(AuthenticationState.SignedIn, service.Current.State);
        Assert.DoesNotContain("/v1/accounts:update", requests);
    }

    private static FirebaseAuthService CreateService(List<string> requests, Func<HttpRequestMessage, HttpResponseMessage> send)
    {
        var path = Path.Combine(Path.GetTempPath(), $"firebase-{Guid.NewGuid():N}.session");
        var client = new HttpClient(new StubHandler(request => { requests.Add(request.RequestUri!.AbsolutePath); return send(request); }));
        return new FirebaseAuthService(client, "test-firebase-api-key-1234567890", new SecureFirebaseSessionStore(path));
    }

    private static HttpResponseMessage Json(string payload, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
}
