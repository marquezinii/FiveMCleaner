namespace Ralven.App.Services;

public static class AccountTerms
{
    public const string CurrentVersion = "2026-08-02";
}

internal static class FirebaseAuthErrorCodes
{
    public const string GoogleAccountMismatch = "GOOGLE_ACCOUNT_MISMATCH";
    public const string AccountAlreadyHasPassword = "ACCOUNT_ALREADY_HAS_PASSWORD";
    public const string CurrentPasswordInvalid = "CURRENT_PASSWORD_INVALID";
    public const string InvalidMfaCode = "INVALID_MFA_CODE";
    public const string MfaChallengeExpired = "MFA_CHALLENGE_EXPIRED";
    public const string ProviderAlreadyLinked = "PROVIDER_ALREADY_LINKED";
    public const string ProviderNotLinked = "PROVIDER_NOT_LINKED";
    public const string LastSignInMethod = "LAST_SIGN_IN_METHOD";
}

public enum AuthenticationState
{
    SignedOut,
    SigningIn,
    MfaChallengeRequired,
    EmailVerificationRequired,
    ProfileCompletionRequired,
    ProfileUnavailable,
    SignedIn,
    RefreshingSession,
    ReauthenticationRequired
}

public enum FirebaseMfaFactorType
{
    Totp,
    Phone,
    Unknown
}

public sealed record FirebaseMfaEnrollment(
    string Id,
    string? DisplayName,
    DateTimeOffset? EnrolledAt,
    FirebaseMfaFactorType FactorType);

public sealed record FirebaseMfaChallenge(string PendingCredential, IReadOnlyList<FirebaseMfaEnrollment> Enrollments);

public sealed record TotpEnrollmentStartResult(
    string? SharedSecretKey,
    string? SessionInfo,
    int VerificationCodeLength,
    string? HashingAlgorithm,
    int PeriodSeconds,
    DateTimeOffset? ExpiresAt,
    string? Error = null)
{
    public bool Succeeded => Error is null
        && !string.IsNullOrWhiteSpace(SharedSecretKey)
        && !string.IsNullOrWhiteSpace(SessionInfo);
}

public sealed record FirebaseUser(
    string Uid,
    string Email,
    bool EmailVerified,
    bool HasPassword = true,
    bool HasGoogle = false,
    IReadOnlyList<FirebaseMfaEnrollment>? MfaEnrollments = null)
{
    public string DisplayName => Email;
    public string Initials => Email[..1].ToUpperInvariant();
    public IReadOnlyList<FirebaseMfaEnrollment> Factors => MfaEnrollments ?? [];
}

public sealed record AuthenticationSnapshot(AuthenticationState State, FirebaseUser? User);

public sealed record FirebaseAuthResult(AuthenticationState State, FirebaseUser? User, string? Error = null)
{
    public bool AccountDeleted { get; init; }
    public FirebaseMfaChallenge? MfaChallenge { get; init; }
    public bool Succeeded => Error is null && (AccountDeleted && State == AuthenticationState.SignedOut
        || User is not null && State is not AuthenticationState.MfaChallengeRequired
            and not AuthenticationState.SigningIn
            and not AuthenticationState.RefreshingSession
            and not AuthenticationState.ReauthenticationRequired);
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
    Task<FirebaseAuthResult> CompleteMfaSignInAsync(string enrollmentId, string verificationCode, bool keepSignedIn, CancellationToken cancellationToken = default) =>
        Task.FromException<FirebaseAuthResult>(new NotSupportedException());
    Task<FirebaseAuthResult> CompleteMfaReauthenticationAsync(string enrollmentId, string verificationCode, CancellationToken cancellationToken = default) =>
        Task.FromException<FirebaseAuthResult>(new NotSupportedException());

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
    Task<FirebaseAuthResult> ReauthenticateWithPasswordAsync(string currentPassword, CancellationToken cancellationToken = default) =>
        Task.FromException<FirebaseAuthResult>(new NotSupportedException());
    Task<FirebaseAuthResult> ReauthenticateWithGoogleAsync(string googleIdToken, CancellationToken cancellationToken = default);
    Task<FirebaseAuthResult> LinkGoogleAsync(string googleIdToken, CancellationToken cancellationToken = default) =>
        Task.FromException<FirebaseAuthResult>(new NotSupportedException());
    Task<FirebaseAuthResult> UnlinkGoogleAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<FirebaseAuthResult>(new NotSupportedException());
    Task<FirebaseAuthResult> CreatePasswordAsync(string newPassword, CancellationToken cancellationToken = default);
    Task<FirebaseAuthResult> UnlinkPasswordAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<FirebaseAuthResult>(new NotSupportedException());
    Task<FirebaseAuthResult> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default);
    Task<FirebaseAuthResult> UpdatePasswordAfterReauthenticationAsync(string newPassword, CancellationToken cancellationToken = default) =>
        Task.FromException<FirebaseAuthResult>(new NotSupportedException());
    Task<FirebaseAuthResult> ChangeEmailAsync(string currentPassword, string newEmail, CancellationToken cancellationToken = default);
    Task<FirebaseAuthResult> RequestEmailChangeAfterReauthenticationAsync(string newEmail, CancellationToken cancellationToken = default) =>
        Task.FromException<FirebaseAuthResult>(new NotSupportedException());
    Task<FirebaseAuthResult> ChangeEmailWithGoogleAsync(string googleIdToken, string newEmail, CancellationToken cancellationToken = default) =>
        Task.FromException<FirebaseAuthResult>(new NotSupportedException());
    Task<FirebaseAuthResult> DeleteAccountAsync(string currentPassword, CancellationToken cancellationToken = default);
    Task<FirebaseAuthResult> DeleteAccountAfterReauthenticationAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<FirebaseAuthResult>(new NotSupportedException());
    Task<FirebaseAuthResult> DeleteAccountWithGoogleAsync(string googleIdToken, CancellationToken cancellationToken = default) =>
        Task.FromException<FirebaseAuthResult>(new NotSupportedException());
    Task<TotpEnrollmentStartResult> StartTotpEnrollmentAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<TotpEnrollmentStartResult>(new NotSupportedException());
    Task<FirebaseAuthResult> FinalizeTotpEnrollmentAsync(string sessionInfo, string verificationCode, string? displayName = null, CancellationToken cancellationToken = default) =>
        Task.FromException<FirebaseAuthResult>(new NotSupportedException());
    Task<FirebaseAuthResult> WithdrawMfaEnrollmentAsync(string enrollmentId, CancellationToken cancellationToken = default) =>
        Task.FromException<FirebaseAuthResult>(new NotSupportedException());
    Task<string?> GetIdTokenAsync(CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
}

internal sealed record FirebaseTokenResponse(
    string? localId,
    string? idToken,
    string? refreshToken,
    string? expiresIn,
    string? mfaPendingCredential = null,
    FirebaseMfaEnrollmentResponse[]? mfaInfo = null);
internal sealed record FirebaseIdpResponse(
    string? localId,
    string? idToken,
    string? refreshToken,
    string? expiresIn,
    string? firstName,
    string? lastName,
    bool isNewUser,
    string? mfaPendingCredential = null,
    FirebaseMfaEnrollmentResponse[]? mfaInfo = null)
{
    public FirebaseTokenResponse ToTokens() => new(localId, idToken, refreshToken, expiresIn, mfaPendingCredential, mfaInfo);
}
internal sealed record FirebaseLookupResponse(FirebaseLookupUser[]? users);
internal sealed record FirebaseLookupUser(string? localId, string? email, bool emailVerified, FirebaseProviderInfo[]? providerUserInfo, FirebaseMfaEnrollmentResponse[]? mfaInfo = null);
internal sealed record FirebaseProviderInfo(string? providerId);
internal sealed record FirebaseMfaEnrollmentResponse(
    string? mfaEnrollmentId,
    string? displayName,
    string? enrolledAt,
    object? totpInfo = null,
    string? phoneInfo = null);
internal sealed record FirebaseMfaTokenResponse(string? idToken, string? refreshToken);
internal sealed record FirebaseTotpEnrollmentStartResponse(FirebaseTotpSessionInfo? totpSessionInfo);
internal sealed record FirebaseTotpSessionInfo(
    string? sharedSecretKey,
    int verificationCodeLength,
    string? hashingAlgorithm,
    int periodSec,
    string? sessionInfo,
    string? finalizeEnrollmentTime);
internal sealed record FirebaseErrorEnvelope(FirebaseError? error);
internal sealed record FirebaseError(string? message);
internal sealed record FirebaseRefreshResponse(string? user_id, string? id_token, string? refresh_token, string? expires_in);
internal sealed record PersistedFirebaseSession(string RefreshToken);
