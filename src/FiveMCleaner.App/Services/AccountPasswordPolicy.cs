namespace FiveMCleaner.App.Services;

public static class AccountPasswordPolicy
{
    public const int MinimumLength = 12;
    public const int MaximumLength = 128;

    public static PasswordRequirements Evaluate(string? password)
    {
        password ??= string.Empty;
        return new PasswordRequirements(
            password.Length >= MinimumLength && password.Length <= MaximumLength,
            true,
            true,
            true,
            true);
    }

    public static bool IsValid(string? password) => Evaluate(password).IsSatisfied;
}

public sealed record PasswordRequirements(bool HasMinimumLength, bool HasUppercase, bool HasLowercase, bool HasNumber, bool HasSpecialCharacter)
{
    public int CompletedCount => new[] { HasMinimumLength, HasUppercase, HasLowercase, HasNumber, HasSpecialCharacter }.Count(value => value);
    public bool IsSatisfied => HasMinimumLength;
}
