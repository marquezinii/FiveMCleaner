namespace Ralven.App.Services;

/// <summary>
/// Stable preference values persisted by older releases. New languages are
/// discovered from localization/locales.json and do not require another enum.
/// </summary>
public static class AppLanguagePreference
{
    public const string Automatic = "automatic";
    public const string English = "english";
    public const string PortugueseBrazil = "portugueseBrazil";
    public const string Spanish = "spanish";
}

public static class AppLanguage
{
    public const string English = "en-US";
    public const string PortugueseBrazil = "pt-BR";
    public const string Spanish = "es-ES";
}

public sealed record SupportedLanguage(string CultureName, string DisplayName);

public sealed class AppLanguageChangedEventArgs : EventArgs
{
    public AppLanguageChangedEventArgs(
        string previousLanguage,
        string currentLanguage,
        string preference)
    {
        PreviousLanguage = previousLanguage;
        CurrentLanguage = currentLanguage;
        Preference = preference;
    }

    public string PreviousLanguage { get; }

    public string CurrentLanguage { get; }

    public string Preference { get; }
}
