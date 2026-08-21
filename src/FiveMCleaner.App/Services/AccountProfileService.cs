namespace FiveMCleaner.App.Services;

public sealed record AccountProfileSubmission
{
    public required string Username { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public required string TermsVersion { get; init; }
}

public enum AccountProfileOutcome
{
    Created,
    UsernameTaken,
    Failed,
}

public sealed record AccountProfileResult(AccountProfileOutcome Outcome, string? Message);

public enum AccountProfileFetchOutcome
{
    Found,
    NotFound,
    Failed,
}

/// <summary>
/// The caller's own username/first/last-name profile, or the reason it
/// could not be read. <see cref="FirstName"/> and the other fields are only
/// meaningful when <see cref="Outcome"/> is <see cref="AccountProfileFetchOutcome.Found"/>.
/// </summary>
public sealed record AccountProfileFetchResult(
    AccountProfileFetchOutcome Outcome,
    string? Username = null,
    string? FirstName = null,
    string? LastName = null,
    string? TermsVersion = null);

public enum AccountProfileDeletionOutcome
{
    Deleted,
    Failed,
}

public sealed record AccountProfileDeletionResult(AccountProfileDeletionOutcome Outcome);

/// <summary>
/// Verdict of the advisory username probe. <see cref="Unknown"/> means the
/// question could not be answered (offline, rate limited, endpoint not
/// configured) and must never be presented as "available" — the definitive
/// answer still comes from <see cref="IAccountProfileService.CreateAsync"/>,
/// which is what actually holds the uniqueness constraint.
/// </summary>
public enum UsernameAvailability
{
    Available,
    Taken,
    Invalid,
    Unknown,
}

/// <summary>
/// Completes a Firebase account with the fields Firebase Authentication
/// REST does not manage — username, first name, last name — via the
/// Cloudflare Worker's <c>/account/profile</c> route. Called once, right
/// after a successful registration, with the fresh Firebase ID token.
/// </summary>
public interface IAccountProfileService
{
    Task<AccountProfileResult> CreateAsync(
        string idToken,
        AccountProfileSubmission submission,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads back the caller's own profile. Called on login and on quiet
    /// session restore, when the app has a fresh ID token but — unlike right
    /// after registration — no longer has the first name in memory.
    /// </summary>
    Task<AccountProfileFetchResult> FetchAsync(
        string idToken,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes only the caller's profile, scoped server-side to the verified Firebase UID.</summary>
    Task<AccountProfileDeletionResult> DeleteAsync(
        string idToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the Worker whether <paramref name="username"/> is still free, so
    /// the registration form can say so while the user types instead of only
    /// after a Firebase account has already been created. Unauthenticated by
    /// necessity (it runs before the account exists) and rate limited server
    /// side; treat the answer as a hint, never as a reservation.
    /// </summary>
    Task<UsernameAvailability> CheckUsernameAsync(
        string username,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Used when <see cref="RemoteServicesOptions.AccountProfileEndpoint"/> is
/// missing or malformed — reports a clear, honest failure instead of
/// crashing or silently pretending the profile was saved.
/// </summary>
public sealed class DisabledAccountProfileService : IAccountProfileService
{
    public Task<AccountProfileResult> CreateAsync(
        string idToken,
        AccountProfileSubmission submission,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new AccountProfileResult(
            AccountProfileOutcome.Failed,
            "Não foi possível salvar seu perfil agora. Tente novamente mais tarde."));

    public Task<AccountProfileFetchResult> FetchAsync(
        string idToken,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new AccountProfileFetchResult(AccountProfileFetchOutcome.Failed));

    public Task<AccountProfileDeletionResult> DeleteAsync(
        string idToken,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new AccountProfileDeletionResult(AccountProfileDeletionOutcome.Failed));

    public Task<UsernameAvailability> CheckUsernameAsync(
        string username,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(UsernameAvailability.Unknown);
}
