namespace FiveMCleaner.App.Services;

public static class AccountPasswordPolicy
{
    public const int MinimumLength = 12;

    public static PasswordRequirements Evaluate(string? password)
    {
        password ??= string.Empty;
        return new PasswordRequirements(
            password.Length >= MinimumLength,
            password.Any(char.IsUpper),
            password.Any(char.IsLower),
            password.Any(char.IsDigit),
            password.Any(character => !char.IsLetterOrDigit(character) && !char.IsWhiteSpace(character)));
    }

    public static bool IsValid(string? password) => Evaluate(password).IsSatisfied;
}

public sealed record PasswordRequirements(bool HasMinimumLength, bool HasUppercase, bool HasLowercase, bool HasNumber, bool HasSpecialCharacter)
{
    public int CompletedCount => new[] { HasMinimumLength, HasUppercase, HasLowercase, HasNumber, HasSpecialCharacter }.Count(value => value);
    public bool IsSatisfied => CompletedCount == 5;
}
