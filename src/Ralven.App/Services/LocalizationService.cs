using System.ComponentModel;
using Ralven.Contracts;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Resources;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ralven.App.Services;

public interface ILocalizationService
{
    event EventHandler<AppLanguageChangedEventArgs>? LanguageChanged;

    string CurrentLanguage { get; }

    string CurrentPreference { get; }

    CultureInfo CurrentCulture { get; }

    string this[string key] { get; }

    string GetString(string key);

    string Format(string key, params object?[] arguments);

    string DescribeException(Exception exception);

    void Apply(string preference, CultureInfo? systemUiCulture = null);

    void SetLanguage(string cultureName);
}

public static class LocalizationCatalog
{
    private const string ManifestResourceName = "Ralven.App.Resources.locales.json";
    private static readonly LocalizationManifest Manifest = LoadManifest();
    private static readonly IReadOnlyDictionary<string, SupportedLanguage> LanguagesByName =
        Manifest.Languages.ToDictionary(
            language => language.Culture,
            language => new SupportedLanguage(language.Culture, language.DisplayName),
            StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyDictionary<string, string> CultureByAlias = BuildAliases();

    public static string SourceCultureName => Manifest.SourceCulture;

    public static string PseudoCultureName => Manifest.PseudoCulture;

    public static IReadOnlyList<SupportedLanguage> SupportedLanguages { get; } = Manifest.Languages
        .Select(language => LanguagesByName[language.Culture])
        .ToArray();

    public static bool TryNormalizePreference(string? preference, out string normalized)
    {
        if (string.IsNullOrWhiteSpace(preference)
            || preference.Equals(AppLanguagePreference.Automatic, StringComparison.OrdinalIgnoreCase)
            || preference.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            normalized = AppLanguagePreference.Automatic;
            return true;
        }

        if (preference.Equals(Manifest.PseudoCulture, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Manifest.PseudoCulture;
            return true;
        }

        return CultureByAlias.TryGetValue(preference, out normalized!);
    }

    public static string NormalizePreference(string? preference) =>
        TryNormalizePreference(preference, out var normalized)
            ? normalized
            : AppLanguagePreference.Automatic;

    public static string DetectLanguage(CultureInfo? systemUiCulture)
    {
        if (systemUiCulture is not null)
        {
            if (LanguagesByName.TryGetValue(systemUiCulture.Name, out var exact))
            {
                return exact.CultureName;
            }

            var languageMatch = SupportedLanguages.FirstOrDefault(language =>
                CultureInfo.GetCultureInfo(language.CultureName).TwoLetterISOLanguageName.Equals(
                    systemUiCulture.TwoLetterISOLanguageName,
                    StringComparison.OrdinalIgnoreCase));
            if (languageMatch is not null)
            {
                return languageMatch.CultureName;
            }
        }

        return Manifest.SourceCulture;
    }

    public static string Resolve(string preference, CultureInfo? systemUiCulture = null)
    {
        if (!TryNormalizePreference(preference, out var normalized))
        {
            throw new ArgumentOutOfRangeException(nameof(preference));
        }

        return normalized == AppLanguagePreference.Automatic
            ? DetectLanguage(systemUiCulture ?? CultureInfo.CurrentUICulture)
            : normalized;
    }

    public static CultureInfo CultureFor(string cultureName) =>
        CultureInfo.GetCultureInfo(
            cultureName.Equals(Manifest.PseudoCulture, StringComparison.OrdinalIgnoreCase)
                ? Manifest.SourceCulture
                : LanguagesByName.TryGetValue(cultureName, out var language)
                    ? language.CultureName
                    : Manifest.SourceCulture);

    private static IReadOnlyDictionary<string, string> BuildAliases()
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var language in Manifest.Languages)
        {
            aliases.Add(language.Culture, language.Culture);
            foreach (var alias in language.Aliases)
            {
                aliases.Add(alias, language.Culture);
            }
        }

        return aliases;
    }

    private static LocalizationManifest LoadManifest()
    {
        using var stream = typeof(LocalizationCatalog).Assembly.GetManifestResourceStream(ManifestResourceName)
            ?? throw new InvalidOperationException("The localization manifest is missing.");
        var manifest = JsonSerializer.Deserialize<LocalizationManifest>(
            stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("The localization manifest is invalid.");
        if (manifest.Languages.Count == 0
            || !manifest.Languages.Any(language => language.Culture.Equals(
                manifest.SourceCulture,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("The localization manifest has no valid source culture.");
        }

        foreach (var language in manifest.Languages)
        {
            _ = CultureInfo.GetCultureInfo(language.Culture);
        }

        return manifest;
    }

    private sealed record LocalizationManifest(
        string SourceCulture,
        string PseudoCulture,
        IReadOnlyList<LocalizationLanguage> Languages);

    private sealed record LocalizationLanguage(
        string Culture,
        string DisplayName,
        IReadOnlyList<string> Aliases);
}

/// <summary>
/// Resolução de recurso com fallback. <see cref="ILocalizationService.GetString"/>
/// devolve a própria chave quando ela não existe no catálogo do idioma atual;
/// quase toda a apresentação precisa, nesse caso, cair para o texto que já veio
/// do catálogo de ações ou dos metadados. Concentrar a convenção aqui evita que
/// cada tela reimplemente a comparação "valor == chave".
/// </summary>
internal static class LocalizationFallback
{
    public static string GetStringOrFallback(
        this ILocalizationService localization,
        string key,
        string fallback)
    {
        var value = localization.GetString(key);
        return value == key ? fallback : value;
    }
}

/// <summary>
/// Runtime localization facade. It deliberately owns its culture instead of
/// mutating process-wide CultureInfo state, so background operations and logs
/// keep deterministic formatting.
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    private const string ResourceBaseName = "Ralven.App.Resources.Strings";
    private static readonly CultureInfo SourceCulture = CultureInfo.GetCultureInfo(
        LocalizationCatalog.SourceCultureName);
    private static readonly ResourceManager Resources = new(
        ResourceBaseName,
        typeof(LocalizationService).Assembly);

    private readonly object sync = new();
    private string currentLanguage;
    private string currentPreference;

    public LocalizationService(CultureInfo? systemUiCulture = null)
    {
        currentPreference = AppLanguagePreference.Automatic;
        currentLanguage = LocalizationCatalog.DetectLanguage(systemUiCulture ?? CultureInfo.CurrentUICulture);
    }

    public static LocalizationService Current { get; } = new();

    public event EventHandler<AppLanguageChangedEventArgs>? LanguageChanged;

    public string CurrentLanguage
    {
        get
        {
            lock (sync)
            {
                return currentLanguage;
            }
        }
    }

    public string CurrentPreference
    {
        get
        {
            lock (sync)
            {
                return currentPreference;
            }
        }
    }

    public CultureInfo CurrentCulture => LocalizationCatalog.CultureFor(CurrentLanguage);

    public string this[string key] => GetString(key);

    public string GetString(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (CurrentLanguage.Equals(LocalizationCatalog.PseudoCultureName, StringComparison.OrdinalIgnoreCase))
        {
            var source = Resources.GetString(key, SourceCulture);
            return string.IsNullOrEmpty(source) ? key : PseudoLocalization.Transform(source);
        }

        var localized = Resources.GetString(key, CurrentCulture);
        if (!string.IsNullOrEmpty(localized))
        {
            return localized;
        }

        var englishFallback = Resources.GetString(key, SourceCulture);
        return string.IsNullOrEmpty(englishFallback) ? key : englishFallback;
    }

    public string Format(string key, params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return string.Format(CurrentCulture, GetString(key), arguments);
    }

    public string FormatCurrency(decimal amount, string currencyCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currencyCode);
        var culture = (CultureInfo)CurrentCulture.Clone();
        culture.NumberFormat.CurrencySymbol = currencyCode.Equals("BRL", StringComparison.OrdinalIgnoreCase)
            ? "R$"
            : currencyCode.ToUpperInvariant();
        return amount.ToString("C", culture);
    }

    public string DescribeException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return GetString(exception switch
        {
            HttpRequestException => "Error.Network",
            TimeoutException => "Error.Timeout",
            UnauthorizedAccessException or SecurityException => "Error.AccessDenied",
            BrokerIntegrityException => "Error.SecurityCheck",
            IOException => "Error.FileUnavailable",
            UpdateSecurityException or CryptographicException => "Error.SecurityCheck",
            InvalidDataException => "Error.InvalidData",
            _ => "Error.Unexpected"
        });
    }

    public void Apply(string preference, CultureInfo? systemUiCulture = null)
    {
        if (!LocalizationCatalog.TryNormalizePreference(preference, out var normalized))
        {
            throw new ArgumentOutOfRangeException(nameof(preference));
        }

        var resolved = LocalizationCatalog.Resolve(normalized, systemUiCulture ?? CultureInfo.CurrentUICulture);
        string previous;
        var shouldNotify = false;
        lock (sync)
        {
            previous = currentLanguage;
            shouldNotify = currentLanguage != resolved;
            currentLanguage = resolved;
            currentPreference = normalized;
        }

        if (shouldNotify)
        {
            LanguageChanged?.Invoke(
                this,
                new AppLanguageChangedEventArgs(previous, resolved, normalized));
        }
    }

    public void SetLanguage(string cultureName)
    {
        Apply(cultureName);
    }
}

internal static class PseudoLocalization
{
    private const string Accented = "åƀçđëƒĝĥïĵķľɱñôþɋřšŧüṽŵẋÿžÅƁÇĐËƑĜĤÏĴĶĽṀÑÔÞɊŘŠŦÜṼŴẊŸŽ";
    private const string Plain = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static string Transform(string value)
    {
        var result = new StringBuilder(value.Length * 2).Append('⟦');
        var letters = 0;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '{' && value.IndexOf('}', index + 1) is var closing && closing >= 0)
            {
                result.Append(value, index, closing - index + 1);
                index = closing;
                continue;
            }

            var mapped = Plain.IndexOf(value[index]);
            result.Append(mapped >= 0 ? Accented[mapped] : value[index]);
            if (mapped >= 0 && ++letters % 4 == 0)
            {
                result.Append('~');
            }
        }

        return result.Append('⟧').ToString();
    }
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
