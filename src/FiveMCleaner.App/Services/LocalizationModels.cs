using System.Globalization;

namespace FiveMCleaner.App.Services;

/// <summary>
/// Preference persisted in settings. Automatic is an internal first-run state;
/// the language picker can expose only English and PortugueseBrazil.
/// </summary>
public enum AppLanguagePreference
{
    Automatic,
    English,
    PortugueseBrazil
}

public enum AppLanguage
{
    English,
    PortugueseBrazil
}

public sealed record AppLanguageOption(
    AppLanguage Language,
    string CultureName,
    string NativeDisplayName)
{
    public CultureInfo Culture => CultureInfo.GetCultureInfo(CultureName);
}

public sealed class AppLanguageChangedEventArgs : EventArgs
{
    public AppLanguageChangedEventArgs(
        AppLanguage previousLanguage,
        AppLanguage currentLanguage,
        AppLanguagePreference preference)
    {
        PreviousLanguage = previousLanguage;
        CurrentLanguage = currentLanguage;
        Preference = preference;
    }

    public AppLanguage PreviousLanguage { get; }

    public AppLanguage CurrentLanguage { get; }

    public AppLanguagePreference Preference { get; }
}
