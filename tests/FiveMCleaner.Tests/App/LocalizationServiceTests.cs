using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.Json;
using FiveMCleaner.App.Services;
using FiveMCleaner.Contracts;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class LocalizationServiceTests
{
    [Theory]
    [InlineData("pt-BR", AppLanguage.PortugueseBrazil)]
    [InlineData("pt-PT", AppLanguage.PortugueseBrazil)]
    [InlineData("en-US", AppLanguage.English)]
    [InlineData("es-ES", AppLanguage.English)]
    [InlineData("fr-FR", AppLanguage.English)]
    public void AutomaticDetection_UsesPortugueseForPtAndEnglishAsFallback(
        string cultureName,
        AppLanguage expected)
    {
        var service = new LocalizationService(CultureInfo.GetCultureInfo(cultureName));

        Assert.Equal(expected, service.CurrentLanguage);
        Assert.Equal(AppLanguagePreference.Automatic, service.CurrentPreference);
    }

    [Fact]
    public void RuntimeChange_RefreshesCultureStringsAndBindingIndexer()
    {
        var service = new LocalizationService(CultureInfo.GetCultureInfo("en-US"));
        using var bindingSource = new LocalizedStrings(service);
        var notifications = new List<string?>();
        AppLanguageChangedEventArgs? languageChange = null;
        bindingSource.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        service.LanguageChanged += (_, args) => languageChange = args;

        Assert.Equal("Overview", bindingSource["Navigation.Overview"]);

        service.SetLanguage(AppLanguage.PortugueseBrazil);

        Assert.Equal("Visão geral", bindingSource["Navigation.Overview"]);
        Assert.Equal("pt-BR", service.CurrentCulture.Name);
        Assert.Equal(AppLanguagePreference.PortugueseBrazil, service.CurrentPreference);
        Assert.Contains("Item[]", notifications);
        Assert.NotNull(languageChange);
        Assert.Equal(AppLanguage.English, languageChange!.PreviousLanguage);
        Assert.Equal(AppLanguage.PortugueseBrazil, languageChange.CurrentLanguage);
    }

    [Fact]
    public void ExplicitPreference_WinsOverSystemCulture()
    {
        var service = new LocalizationService(CultureInfo.GetCultureInfo("pt-BR"));

        service.Apply(
            AppLanguagePreference.English,
            CultureInfo.GetCultureInfo("pt-BR"));

        Assert.Equal(AppLanguage.English, service.CurrentLanguage);
        Assert.Equal("Settings", service["Settings.Title"]);
    }

    [Fact]
    public void ManualChoice_IsPersistedEvenWhenItMatchesTheDetectedLanguage()
    {
        var service = new LocalizationService(CultureInfo.GetCultureInfo("pt-BR"));

        Assert.Equal(AppLanguagePreference.Automatic, service.CurrentPreference);
        Assert.Equal(AppLanguage.PortugueseBrazil, service.CurrentLanguage);

        service.SetLanguage(AppLanguage.PortugueseBrazil);

        Assert.Equal(AppLanguagePreference.PortugueseBrazil, service.CurrentPreference);
        Assert.Equal(AppLanguage.PortugueseBrazil, service.CurrentLanguage);
    }

    [Fact]
    public void MissingResource_FallsBackToStableKey()
    {
        var service = new LocalizationService(CultureInfo.GetCultureInfo("pt-BR"));

        Assert.Equal("Missing.Key", service.GetString("Missing.Key"));
    }

    [Fact]
    public void Format_UsesSelectedCultureAndLocalizedTemplate()
    {
        var service = new LocalizationService(CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal(
            "Version 0.2.0  •  Developed by Felipe Marquezini",
            service.Format("About.VersionDeveloper", "0.2.0"));

        service.SetLanguage(AppLanguage.PortugueseBrazil);

        Assert.Equal(
            "Versão 0.2.0  •  Desenvolvido por Felipe Marquezini",
            service.Format("About.VersionDeveloper", "0.2.0"));
    }

    [Fact]
    public void EnglishAndPortugueseCatalogs_HaveExactlyTheSameKeys()
    {
        var manager = new ResourceManager(
            "FiveMCleaner.App.Resources.Strings",
            typeof(LocalizationService).Assembly);
        using var english = manager.GetResourceSet(
            CultureInfo.GetCultureInfo("en-US"),
            createIfNotExists: true,
            tryParents: true);
        using var portuguese = manager.GetResourceSet(
            CultureInfo.GetCultureInfo("pt-BR"),
            createIfNotExists: true,
            tryParents: false);

        Assert.NotNull(english);
        Assert.NotNull(portuguese);
        var englishKeys = KeysOf(english!);
        var portugueseKeys = KeysOf(portuguese!);

        Assert.True(englishKeys.Count >= 100);
        Assert.Equal(englishKeys, portugueseKeys);
    }

    [Fact]
    public void ExistingSettingsJson_DefaultsToAutomaticLanguage()
    {
        const string previousVersionJson = "{\"theme\":\"dark\"}";

        var settings = JsonSerializer.Deserialize<AppSettings>(
            previousVersionJson,
            FiveMCleanerJson.Options);

        Assert.NotNull(settings);
        Assert.Equal(AppLanguagePreference.Automatic, settings!.Language);
        Assert.Equal(AppThemePreference.Dark, settings.Theme);
    }

    [Fact]
    public void LanguagePreference_RoundTripsThroughSettingsJson()
    {
        var source = new AppSettings
        {
            Language = AppLanguagePreference.PortugueseBrazil,
            Theme = AppThemePreference.System
        };

        var json = JsonSerializer.Serialize(source, FiveMCleanerJson.Options);
        var result = JsonSerializer.Deserialize<AppSettings>(json, FiveMCleanerJson.Options);

        Assert.Contains("\"language\":\"portugueseBrazil\"", json, StringComparison.Ordinal);
        Assert.NotNull(result);
        Assert.Equal(AppLanguagePreference.PortugueseBrazil, result!.Language);
    }

    private static SortedSet<string> KeysOf(ResourceSet resourceSet)
    {
        return resourceSet.Cast<DictionaryEntry>()
            .Select(entry => Assert.IsType<string>(entry.Key))
            .ToSortedSet(StringComparer.Ordinal);
    }
}

internal static class LocalizationTestEnumerableExtensions
{
    public static SortedSet<T> ToSortedSet<T>(
        this IEnumerable<T> source,
        IComparer<T>? comparer = null)
    {
        return new SortedSet<T>(source, comparer);
    }
}
