namespace FiveMCleaner.App.Services;

public static class AccountPasswordPolicy
{
    public const int MinimumLength = 12;
    public const int MaximumLength = 128;

    public static PasswordRequirements Evaluate(string? password)
    {
        password ??= string.Empty;
        return new PasswordRequirements(
            password.Length >= MinimumLength,
            password.Length <= MaximumLength);
    }

    public static bool IsValid(string? password) => Evaluate(password).IsSatisfied;
}

public sealed record PasswordRequirements(bool HasMinimumLength, bool HasMaximumLength)
{
    public bool IsSatisfied => HasMinimumLength && HasMaximumLength;
}
