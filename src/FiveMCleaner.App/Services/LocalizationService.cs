using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace FiveMCleaner.App.Services;

public interface ILocalizationService
{
    event EventHandler<AppLanguageChangedEventArgs>? LanguageChanged;

    AppLanguage CurrentLanguage { get; }

    AppLanguagePreference CurrentPreference { get; }

    CultureInfo CurrentCulture { get; }

    IReadOnlyList<AppLanguageOption> SupportedLanguages { get; }

    string this[string key] { get; }

    string GetString(string key);

    string Format(string key, params object?[] arguments);

    void Apply(AppLanguagePreference preference, CultureInfo? systemUiCulture = null);

    void SetLanguage(AppLanguage language);
}

/// <summary>
/// Runtime localization facade. It deliberately owns its culture instead of
/// mutating process-wide CultureInfo state, so background operations and logs
/// keep deterministic formatting.
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    private const string ResourceBaseName = "FiveMCleaner.App.Resources.Strings";
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo PortugueseBrazilCulture = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly ResourceManager Resources = new(
        ResourceBaseName,
        typeof(LocalizationService).Assembly);
    private static readonly IReadOnlyList<AppLanguageOption> LanguageOptions = Array.AsReadOnly(
    [
        new AppLanguageOption(AppLanguage.English, EnglishCulture.Name, "English"),
        new AppLanguageOption(AppLanguage.PortugueseBrazil, PortugueseBrazilCulture.Name, "Português (Brasil)")
    ]);

    private readonly object sync = new();
    private AppLanguage currentLanguage;
    private AppLanguagePreference currentPreference;

    public LocalizationService(CultureInfo? systemUiCulture = null)
    {
        currentPreference = AppLanguagePreference.Automatic;
        currentLanguage = DetectLanguage(systemUiCulture ?? CultureInfo.CurrentUICulture);
    }

    public static LocalizationService Current { get; } = new();

    public event EventHandler<AppLanguageChangedEventArgs>? LanguageChanged;

    public AppLanguage CurrentLanguage
    {
        get
        {
            lock (sync)
            {
                return currentLanguage;
            }
        }
    }

    public AppLanguagePreference CurrentPreference
    {
        get
        {
            lock (sync)
            {
                return currentPreference;
            }
        }
    }

    public CultureInfo CurrentCulture => CultureFor(CurrentLanguage);

    public IReadOnlyList<AppLanguageOption> SupportedLanguages => LanguageOptions;

    public string this[string key] => GetString(key);

    public string GetString(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var localized = Resources.GetString(key, CurrentCulture);
        if (!string.IsNullOrEmpty(localized))
        {
            return localized;
        }

        var englishFallback = Resources.GetString(key, EnglishCulture);
        return string.IsNullOrEmpty(englishFallback) ? key : englishFallback;
    }

    public string Format(string key, params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return string.Format(CurrentCulture, GetString(key), arguments);
    }

    public void Apply(AppLanguagePreference preference, CultureInfo? systemUiCulture = null)
    {
        if (!Enum.IsDefined(preference))
        {
            throw new ArgumentOutOfRangeException(nameof(preference));
        }

        var resolved = Resolve(preference, systemUiCulture ?? CultureInfo.CurrentUICulture);
        AppLanguage previous;
        var shouldNotify = false;
        lock (sync)
        {
            previous = currentLanguage;
            shouldNotify = currentLanguage != resolved;
            currentLanguage = resolved;
            currentPreference = preference;
        }

        if (shouldNotify)
        {
            LanguageChanged?.Invoke(
                this,
                new AppLanguageChangedEventArgs(previous, resolved, preference));
        }
    }

    public void SetLanguage(AppLanguage language)
    {
        if (!Enum.IsDefined(language))
        {
            throw new ArgumentOutOfRangeException(nameof(language));
        }

        Apply(language switch
        {
            AppLanguage.English => AppLanguagePreference.English,
            AppLanguage.PortugueseBrazil => AppLanguagePreference.PortugueseBrazil,
            _ => throw new ArgumentOutOfRangeException(nameof(language))
        });
    }

    public static AppLanguage DetectLanguage(CultureInfo? systemUiCulture)
    {
        if (systemUiCulture is null)
        {
            return AppLanguage.English;
        }

        return string.Equals(
            systemUiCulture.TwoLetterISOLanguageName,
            "pt",
            StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.PortugueseBrazil
            : AppLanguage.English;
    }

    public static AppLanguage Resolve(
        AppLanguagePreference preference,
        CultureInfo? systemUiCulture = null)
    {
        return preference switch
        {
            AppLanguagePreference.Automatic => DetectLanguage(
                systemUiCulture ?? CultureInfo.CurrentUICulture),
            AppLanguagePreference.English => AppLanguage.English,
            AppLanguagePreference.PortugueseBrazil => AppLanguage.PortugueseBrazil,
            _ => throw new ArgumentOutOfRangeException(nameof(preference))
        };
    }

    private static CultureInfo CultureFor(AppLanguage language) => language switch
    {
        AppLanguage.English => EnglishCulture,
        AppLanguage.PortugueseBrazil => PortugueseBrazilCulture,
        _ => EnglishCulture
    };
}

/// <summary>
/// Binding source for WPF. XAML can use {Binding [Navigation.Overview],
/// Source={StaticResource LocalizedStrings}} and all indexer bindings refresh
/// after a runtime language change.
/// </summary>
public sealed class LocalizedStrings : INotifyPropertyChanged, IDisposable
{
    private readonly ILocalizationService localization;
    private bool disposed;

    public LocalizedStrings()
        : this(LocalizationService.Current)
    {
    }

    public LocalizedStrings(ILocalizationService localization)
    {
        this.localization = localization ?? throw new ArgumentNullException(nameof(localization));
        this.localization.LanguageChanged += OnLanguageChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => localization.GetString(key);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        localization.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, AppLanguageChangedEventArgs e)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
