namespace Ralven.App.Services;

public enum RecoveryCodesOutcome
{
    Created,
    InvalidInput,
    EnrollmentNotFound,
    ReauthenticationRequired,
    RateLimited,
    Unavailable,
    Failed,
}

public sealed record RecoveryCodesResult(
    RecoveryCodesOutcome Outcome,
    IReadOnlyList<string> RecoveryCodes,
    string? ErrorCode = null)
{
    public bool Succeeded => Outcome == RecoveryCodesOutcome.Created;
}

public enum RecoveryCodesDeletionOutcome
{
    Deleted,
    InvalidInput,
    ReauthenticationRequired,
    RateLimited,
    Unavailable,
    Failed,
}

public sealed record RecoveryCodesDeletionResult(
    RecoveryCodesDeletionOutcome Outcome,
    string? ErrorCode = null)
{
    public bool Succeeded => Outcome == RecoveryCodesDeletionOutcome.Deleted;
}

public enum AccountRecoveryOutcome
{
    Recovered,
    InvalidInput,
    InvalidOrUsedCode,
    InProgress,
    ReauthenticationRequired,
    RateLimited,
    Unavailable,
    Failed,
}

public sealed record AccountRecoveryResult(
    AccountRecoveryOutcome Outcome,
    string? ErrorCode = null)
{
    public bool Succeeded => Outcome == AccountRecoveryOutcome.Recovered;
}

public interface IAccountSecurityService
{
    Task<RecoveryCodesResult> GenerateRecoveryCodesAsync(
        string idToken,
        string mfaEnrollmentId,
        CancellationToken cancellationToken = default);

    Task<RecoveryCodesDeletionResult> DeleteRecoveryCodesAsync(
        string idToken,
        string mfaEnrollmentId,
        CancellationToken cancellationToken = default);

    Task<AccountRecoveryResult> RecoverAsync(
        string mfaPendingCredential,
        string mfaEnrollmentId,
        string recoveryCode,
        CancellationToken cancellationToken = default);
}

public sealed class DisabledAccountSecurityService : IAccountSecurityService
{
    private static readonly RecoveryCodesResult CodesUnavailable =
        new(RecoveryCodesOutcome.Unavailable, []);

    public Task<RecoveryCodesResult> GenerateRecoveryCodesAsync(
        string idToken,
        string mfaEnrollmentId,
        CancellationToken cancellationToken = default) => Task.FromResult(CodesUnavailable);

    public Task<AccountRecoveryResult> RecoverAsync(
        string mfaPendingCredential,
        string mfaEnrollmentId,
        string recoveryCode,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new AccountRecoveryResult(AccountRecoveryOutcome.Unavailable));

    public Task<RecoveryCodesDeletionResult> DeleteRecoveryCodesAsync(
        string idToken,
        string mfaEnrollmentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new RecoveryCodesDeletionResult(RecoveryCodesDeletionOutcome.Unavailable));
}
