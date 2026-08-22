namespace FiveMCleaner.App.Services;

public static class AccountTerms
{
    public const string CurrentVersion = "2026-08-02";
}

internal static class FirebaseAuthErrorCodes
{
    public const string GoogleAccountMismatch = "GOOGLE_ACCOUNT_MISMATCH";
    public const string AccountAlreadyHasPassword = "ACCOUNT_ALREADY_HAS_PASSWORD";
    public const string CurrentPasswordInvalid = "CURRENT_PASSWORD_INVALID";
}

public enum AuthenticationState
{
    SignedOut,
    SigningIn,
    EmailVerificationRequired,
    ProfileCompletionRequired,
    SignedIn,
    RefreshingSession,
    ReauthenticationRequired
}

public sealed record FirebaseUser(string Uid, string Email, bool EmailVerified, bool HasPassword = true)
{
    public string DisplayName => Email;
    public string Initials => Email[..1].ToUpperInvariant();
}

public sealed record AuthenticationSnapshot(AuthenticationState State, FirebaseUser? User);

public sealed record FirebaseAuthResult(AuthenticationState State, FirebaseUser? User, string? Error = null)
{
    public bool Succeeded => Error is null && User is not null;
}

/// <summary>
/// Result of signing in through an identity provider (currently Google).
/// Carries the same <see cref="FirebaseAuthResult"/> as the password flows
/// plus the two things only the provider can tell us: whether this is the
/// account's first sign-in — meaning it still needs a username and has no
/// profile row yet — and the names Google already knows, used to prefill
/// the profile step instead of asking the user to retype them.
/// </summary>
public sealed record FederatedSignInResult(
    FirebaseAuthResult Result,
    bool IsNewUser = false,
    string? FirstName = null,
    string? LastName = null);

public interface IFirebaseAuthService : IDisposable
{
    AuthenticationSnapshot Current { get; }
    event EventHandler<AuthenticationSnapshot>? StateChanged;
    Task<FirebaseAuthResult> RestoreSessionAsync(CancellationToken cancellationToken = default);
    Task<FirebaseAuthResult> RegisterAsync(string email, string password, bool keepSignedIn, CancellationToken cancellationToken = default);
    Task<FirebaseAuthResult> SignInAsync(string email, string password, bool keepSignedIn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges a Google OpenID Connect id_token (obtained by
    /// <see cref="IGoogleOAuthClient"/>) for a Firebase session. Google has
    /// already verified the address, so the account never goes through the
    /// e-mail verification step.
    /// </summary>
    Task<FederatedSignInResult> SignInWithGoogleAsync(string googleIdToken, bool keepSignedIn, CancellationToken cancellationToken = default);
    Task<FirebaseAuthResult> RefreshEmailVerificationAsync(CancellationToken cancellationToken = default);
    Task<FirebaseAuthResult> RefreshAccountReadinessAsync(CancellationToken cancellationToken = default);
    Task<FirebaseAuthResult> ResendVerificationEmailAsync(CancellationToken cancellationToken = default);
    Task<FirebaseAuthResult> SendPasswordResetEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<FirebaseAuthResult> ReauthenticateWithGoogleAsync(string googleIdToken, CancellationToken cancellationToken = default);
    Task<FirebaseAuthResult> CreatePasswordAsync(string newPassword, CancellationToken cancellationToken = default);
    Task<FirebaseAuthResult> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default);
    Task<FirebaseAuthResult> ChangeEmailAsync(string currentPassword, string newEmail, CancellationToken cancellationToken = default);
    Task<FirebaseAuthResult> DeleteAccountAsync(string currentPassword, CancellationToken cancellationToken = default);
    Task<string?> GetIdTokenAsync(CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
}

internal sealed record FirebaseTokenResponse(string? localId, string? idToken, string? refreshToken, string? expiresIn);
internal sealed record FirebaseIdpResponse(
    string? localId,
    string? idToken,
    string? refreshToken,
    string? expiresIn,
    string? firstName,
    string? lastName,
    bool isNewUser)
{
    public FirebaseTokenResponse ToTokens() => new(localId, idToken, refreshToken, expiresIn);
}
internal sealed record FirebaseLookupResponse(FirebaseLookupUser[]? users);
internal sealed record FirebaseLookupUser(string? localId, string? email, bool emailVerified, FirebaseProviderInfo[]? providerUserInfo);
internal sealed record FirebaseProviderInfo(string? providerId);
internal sealed record FirebaseErrorEnvelope(FirebaseError? error);
internal sealed record FirebaseError(string? message);
internal sealed record FirebaseRefreshResponse(string? user_id, string? id_token, string? refresh_token, string? expires_in);
internal sealed record PersistedFirebaseSession(string RefreshToken);
