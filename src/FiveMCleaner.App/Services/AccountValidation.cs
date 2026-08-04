using System.Net.Mail;
using System.Text.RegularExpressions;

namespace FiveMCleaner.App.Services;

/// <summary>
/// Client-side validation for account fields. This is a UX convenience only
/// — the Cloudflare Worker re-validates everything server-side with the
/// same rules before ever touching D1 (see
/// <c>infra/cloudflare-worker/src/auth/accountProfile.js</c>), because a
/// client check can always be bypassed.
/// </summary>
public static partial class AccountValidation
{
    public static bool IsValidEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim();
        return MailAddress.TryCreate(trimmed, out var address)
            && string.Equals(address.Address, trimmed, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Starts with a letter, then 2–23 letters/digits/underscores (3–24
    /// total) — no spaces or other symbols, so it can be used unquoted
    /// wherever a username is displayed.
    /// </summary>
    public static bool IsValidUsername(string? value) =>
        !string.IsNullOrWhiteSpace(value) && UsernamePattern().IsMatch(value.Trim());

    /// <summary>Nome/Sobrenome: letters (including accents), spaces, hyphens and apostrophes, 1–60 characters.</summary>
    public static bool IsValidPersonName(string? value) =>
        !string.IsNullOrWhiteSpace(value) && PersonNamePattern().IsMatch(value.Trim());

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9_]{2,23}$")]
    private static partial Regex UsernamePattern();

    [GeneratedRegex(@"^\p{L}[\p{L} '-]{0,59}$")]
    private static partial Regex PersonNamePattern();
}
