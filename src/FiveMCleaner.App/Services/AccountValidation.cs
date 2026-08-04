using System.Net.Mail;

namespace FiveMCleaner.App.Services;

public static class AccountValidation
{
    public static bool IsValidEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim();
        return MailAddress.TryCreate(trimmed, out var address)
            && string.Equals(address.Address, trimmed, StringComparison.OrdinalIgnoreCase);
    }
}
