using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using FiveMCleaner.Contracts;

namespace FiveMCleaner.App.Services;

public sealed class FirebaseAuthService : IFirebaseAuthService
{
    private const string IdentityBase = "https://identitytoolkit.googleapis.com/v1/";
    private const string SecureTokenBase = "https://securetoken.googleapis.com/v1/token";
    private readonly HttpClient client;
    private readonly string apiKey;
    private readonly SecureFirebaseSessionStore sessionStore;
    private string? idToken;
    private string? refreshToken;
    private DateTimeOffset tokenExpiresAt;

    public FirebaseAuthService(string apiKey)
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }, apiKey,
            new SecureFirebaseSessionStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductIdentity.Name, "firebase.session")))
    { }

    internal FirebaseAuthService(HttpClient client, string apiKey, SecureFirebaseSessionStore sessionStore)
    {
        this.client = client;
        this.apiKey = apiKey;
        this.sessionStore = sessionStore;
    }

    public AuthenticationSnapshot Current { get; private set; } = new(AuthenticationState.SignedOut, null);
    public event EventHandler<AuthenticationSnapshot>? StateChanged;

    public async Task<FirebaseAuthResult> RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        var stored = await sessionStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (stored is null || string.IsNullOrWhiteSpace(stored.RefreshToken)) return Result();
        refreshToken = stored.RefreshToken;
        return await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<FirebaseAuthResult> RegisterAsync(string email, string password, bool keepSignedIn, CancellationToken cancellationToken = default)
    {
        SetState(AuthenticationState.SigningIn);
        var response = await PostAsync<FirebaseTokenResponse>("accounts:signUp", new { email, password, returnSecureToken = true }, cancellationToken).ConfigureAwait(false);
        if (response.Error is not null) return Fail(response.Error);
        var result = await AcceptTokensAsync(response.Value!, keepSignedIn, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded) await ResendVerificationEmailAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<FirebaseAuthResult> SignInAsync(string email, string password, bool keepSignedIn, CancellationToken cancellationToken = default)
    {
        SetState(AuthenticationState.SigningIn);
        var response = await PostAsync<FirebaseTokenResponse>("accounts:signInWithPassword", new { email, password, returnSecureToken = true }, cancellationToken).ConfigureAwait(false);
        return response.Error is null ? await AcceptTokensAsync(response.Value!, keepSignedIn, cancellationToken).ConfigureAwait(false) : Fail(response.Error, sensitiveFlow: true);
    }

    public async Task<FirebaseAuthResult> RefreshEmailVerificationAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetIdTokenAsync(cancellationToken).ConfigureAwait(false);
        if (token is null) return Result();
        return await LoadUserAsync(token, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FirebaseAuthResult> ResendVerificationEmailAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetIdTokenAsync(cancellationToken).ConfigureAwait(false);
        if (token is null) return Result();
        var response = await PostAsync<object>("accounts:sendOobCode", new { requestType = "VERIFY_EMAIL", idToken = token }, cancellationToken).ConfigureAwait(false);
        return response.Error is null ? Result() : Fail(response.Error, sensitiveFlow: true);
    }

    public async Task<FirebaseAuthResult> SendPasswordResetEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var response = await PostAsync<object>("accounts:sendOobCode", new { requestType = "PASSWORD_RESET", email }, cancellationToken).ConfigureAwait(false);
        return response.Error is null ? Result() : new FirebaseAuthResult(Current.State, Current.User, FirebaseAuthErrorMapper.Map(response.Error, true));
    }

    public async Task<FirebaseAuthResult> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        if (!await ReauthenticateAsync(currentPassword, cancellationToken).ConfigureAwait(false)) return new FirebaseAuthResult(AuthenticationState.ReauthenticationRequired, Current.User, "Confirme sua senha para concluir esta alteração.");
        return await UpdateAsync(newPassword, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FirebaseAuthResult> ChangeEmailAsync(string currentPassword, string newEmail, CancellationToken cancellationToken = default)
    {
        if (!await ReauthenticateAsync(currentPassword, cancellationToken).ConfigureAwait(false)) return new FirebaseAuthResult(AuthenticationState.ReauthenticationRequired, Current.User, "Confirme sua senha para concluir esta alteração.");
        var result = await UpdateAsync(null, newEmail, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded) await ResendVerificationEmailAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<FirebaseAuthResult> DeleteAccountAsync(string currentPassword, CancellationToken cancellationToken = default)
    {
        if (!await ReauthenticateAsync(currentPassword, cancellationToken).ConfigureAwait(false)) return new FirebaseAuthResult(AuthenticationState.ReauthenticationRequired, Current.User, "Confirme sua senha para excluir a conta.");
        var token = await GetIdTokenAsync(cancellationToken).ConfigureAwait(false);
        if (token is null) return Result();
        var response = await PostAsync<object>("accounts:delete", new { idToken = token }, cancellationToken).ConfigureAwait(false);
        if (response.Error is not null) return Fail(response.Error);
        await LogoutAsync(cancellationToken).ConfigureAwait(false);
        return Result();
    }

    public async Task<string?> GetIdTokenAsync(CancellationToken cancellationToken = default)
    {
        if (Current.User is null) return null;
        if (DateTimeOffset.UtcNow >= tokenExpiresAt - TimeSpan.FromMinutes(5))
        {
            var refreshed = await RefreshAsync(cancellationToken).ConfigureAwait(false);
            if (!refreshed.Succeeded) return null;
        }
        return idToken;
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        idToken = refreshToken = null;
        tokenExpiresAt = default;
        await sessionStore.ClearAsync().ConfigureAwait(false);
        SetState(AuthenticationState.SignedOut);
    }

    private async Task<FirebaseAuthResult> RefreshAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) { SetState(AuthenticationState.SignedOut); return Result(); }
        SetState(AuthenticationState.RefreshingSession);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{SecureTokenBase}?key={apiKey}") { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }) };
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await response.Content.ReadFromJsonAsync<FirebaseRefreshResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || payload?.id_token is null || payload.refresh_token is null) { await LogoutAsync(cancellationToken).ConfigureAwait(false); return new FirebaseAuthResult(AuthenticationState.SignedOut, null, "Sua sessão não é mais válida. Entre novamente."); }
            idToken = payload.id_token; refreshToken = payload.refresh_token; tokenExpiresAt = Expiry(payload.expires_in);
            await sessionStore.WriteAsync(refreshToken, cancellationToken).ConfigureAwait(false);
            return await LoadUserAsync(idToken, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Fail("NETWORK_REQUEST_FAILED"); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException) { return Fail("NETWORK_REQUEST_FAILED"); }
    }

    private async Task<FirebaseAuthResult> UpdateAsync(string? password, string? email, CancellationToken cancellationToken)
    {
        var token = await GetIdTokenAsync(cancellationToken).ConfigureAwait(false);
        if (token is null) return Result();
        var response = await PostAsync<FirebaseTokenResponse>("accounts:update", new { idToken = token, returnSecureToken = true, password, email }, cancellationToken).ConfigureAwait(false);
        return response.Error is null ? await AcceptTokensAsync(response.Value!, refreshToken is not null, cancellationToken).ConfigureAwait(false) : Fail(response.Error);
    }

    private async Task<bool> ReauthenticateAsync(string password, CancellationToken cancellationToken)
    {
        if (Current.User is null) return false;
        SetState(AuthenticationState.ReauthenticationRequired);
        var response = await PostAsync<FirebaseTokenResponse>("accounts:signInWithPassword", new { email = Current.User.Email, password, returnSecureToken = true }, cancellationToken).ConfigureAwait(false);
        if (response.Error is not null || !string.Equals(response.Value?.localId, Current.User.Uid, StringComparison.Ordinal)) return false;
        return (await AcceptTokensAsync(response.Value!, refreshToken is not null, cancellationToken).ConfigureAwait(false)).Succeeded;
    }

    private async Task<FirebaseAuthResult> AcceptTokensAsync(FirebaseTokenResponse tokens, bool persist, CancellationToken cancellationToken)
    {
        if (tokens.idToken is null || tokens.refreshToken is null) return Fail("INVALID_ID_TOKEN");
        idToken = tokens.idToken; refreshToken = tokens.refreshToken; tokenExpiresAt = Expiry(tokens.expiresIn);
        if (persist) await sessionStore.WriteAsync(refreshToken, cancellationToken).ConfigureAwait(false); else await sessionStore.ClearAsync().ConfigureAwait(false);
        return await LoadUserAsync(idToken, cancellationToken).ConfigureAwait(false);
    }

    private async Task<FirebaseAuthResult> LoadUserAsync(string token, CancellationToken cancellationToken)
    {
        var response = await PostAsync<FirebaseLookupResponse>("accounts:lookup", new { idToken = token }, cancellationToken).ConfigureAwait(false);
        var user = response.Value?.users?.FirstOrDefault();
        if (response.Error is not null || user?.localId is null || user.email is null) return Fail(response.Error ?? "INVALID_ID_TOKEN");
        var firebaseUser = new FirebaseUser(user.localId, user.email, user.emailVerified);
        SetState(firebaseUser.EmailVerified ? AuthenticationState.SignedIn : AuthenticationState.EmailVerificationRequired, firebaseUser);
        return Result();
    }

    private async Task<(T? Value, string? Error)> PostAsync<T>(string path, object body, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.PostAsJsonAsync($"{IdentityBase}{path}?key={apiKey}", body, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return (await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken).ConfigureAwait(false), null);
            var error = await response.Content.ReadFromJsonAsync<FirebaseErrorEnvelope>(cancellationToken: cancellationToken).ConfigureAwait(false);
            return (default, error?.error?.message ?? "UNKNOWN");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return (default, "NETWORK_REQUEST_FAILED"); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException) { return (default, "NETWORK_REQUEST_FAILED"); }
    }

    private FirebaseAuthResult Result() => new(Current.State, Current.User);
    private FirebaseAuthResult Fail(string? error, bool sensitiveFlow = false)
    {
        if (error is "INVALID_ID_TOKEN" or "TOKEN_EXPIRED" or "INVALID_REFRESH_TOKEN" or "USER_DISABLED") _ = LogoutAsync();
        return new FirebaseAuthResult(Current.State, Current.User, FirebaseAuthErrorMapper.Map(error, sensitiveFlow));
    }
    private void SetState(AuthenticationState state, FirebaseUser? user = null)
    {
        Current = new AuthenticationSnapshot(state, user ?? Current.User);
        if (state == AuthenticationState.SignedOut) Current = new AuthenticationSnapshot(state, null);
        StateChanged?.Invoke(this, Current);
    }
    private static DateTimeOffset Expiry(string? seconds) => DateTimeOffset.UtcNow.AddSeconds(long.TryParse(seconds, out var value) ? value : 3600);
    public void Dispose() => client.Dispose();
}
