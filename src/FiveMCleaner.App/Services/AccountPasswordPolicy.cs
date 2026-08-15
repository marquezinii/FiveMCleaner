namespace FiveMCleaner.App.Services;

public static class AccountPasswordPolicy
{
    public const int MinimumLength = 12;
    public const int MaximumLength = 128;

    public static bool IsValid(string? password) =>
        password is not null && password.Length >= MinimumLength && password.Length <= MaximumLength;
}
