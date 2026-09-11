namespace Ralven.App.Services;

internal static class FirebaseAuthErrorMapper
{
    public static string Map(string? code, ILocalizationService localization, bool sensitiveFlow = false) =>
        localization.GetString(code switch
        {
            "TOO_MANY_ATTEMPTS_TRY_LATER" => "Account.Error.TooManyAttempts",
            "NETWORK_REQUEST_FAILED" => "Error.Network",
            "INVALID_ID_TOKEN" or "TOKEN_EXPIRED" or "USER_NOT_FOUND" or "INVALID_REFRESH_TOKEN" or "USER_DISABLED" => "Account.Error.SessionInvalid",
            "CREDENTIAL_TOO_OLD_LOGIN_AGAIN" or "REQUIRES_RECENT_LOGIN" => "Account.Error.ReauthenticationRequired",
            "EMAIL_EXISTS" => "Account.Error.EmailExists",
            "ACCOUNT_EXISTS_WITH_DIFFERENT_CREDENTIAL" => "Account.Error.ProviderConflict",
            "CREDENTIAL_ALREADY_IN_USE" or "FEDERATED_USER_ID_ALREADY_LINKED" => "Account.Error.GoogleAlreadyLinkedElsewhere",
            "WEAK_PASSWORD" or "PASSWORD_DOES_NOT_MEET_REQUIREMENTS" => "Account.Error.WeakPassword",
            "INVALID_EMAIL" => "Account.Validation.InvalidEmail",
            FirebaseAuthErrorCodes.GoogleAccountMismatch => "PasswordSecurity.Validation.GoogleAccountMismatch",
            FirebaseAuthErrorCodes.AccountAlreadyHasPassword => "PasswordSecurity.Validation.AlreadyHasPassword",
            FirebaseAuthErrorCodes.CurrentPasswordInvalid => "PasswordSecurity.Validation.CurrentPasswordInvalid",
            FirebaseAuthErrorCodes.InvalidMfaCode => "Account.Mfa.InvalidCode",
            FirebaseAuthErrorCodes.MfaChallengeExpired => "Account.Mfa.ChallengeExpired",
            FirebaseAuthErrorCodes.ProviderAlreadyLinked => "Settings.Account.GoogleLinkedSuccess",
            FirebaseAuthErrorCodes.ProviderNotLinked => "Settings.Account.GoogleNotLinked",
            FirebaseAuthErrorCodes.LastSignInMethod => "Settings.Account.LastSignInMethod",
            _ when sensitiveFlow => "Account.Error.SensitiveFlow",
            _ => "Account.Error.Generic"
        });
}
