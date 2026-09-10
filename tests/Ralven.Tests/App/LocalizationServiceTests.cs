using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.Json;
using Ralven.App.Services;
using Ralven.Contracts;
using Xunit;

namespace Ralven.Tests.App;

public sealed class LocalizationServiceTests
{
    [Theory]
    [InlineData("pt-BR", AppLanguage.PortugueseBrazil)]
    [InlineData("pt-PT", AppLanguage.PortugueseBrazil)]
    [InlineData("en-US", AppLanguage.English)]
    [InlineData("es-ES", AppLanguage.Spanish)]
    [InlineData("es-MX", AppLanguage.Spanish)]
    [InlineData("fr-FR", "fr-FR")]
    [InlineData("de-DE", AppLanguage.English)]
    public void AutomaticDetection_UsesSupportedLanguageOrEnglishFallback(
        string cultureName,
        string expected)
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
        Assert.Equal(AppLanguage.PortugueseBrazil, service.CurrentPreference);
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

        Assert.Equal(AppLanguage.PortugueseBrazil, service.CurrentPreference);
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
            "Version 0.2.0  •  Ralven",
            service.Format("About.VersionDeveloper", "0.2.0"));

        service.SetLanguage(AppLanguage.PortugueseBrazil);

        Assert.Equal(
            "Versão 0.2.0  •  Ralven",
            service.Format("About.VersionDeveloper", "0.2.0"));
    }

    [Fact]
    public void DescribeException_UsesLocalizedActionableMessagesWithoutLeakingTechnicalDetails()
    {
        var service = new LocalizationService(CultureInfo.GetCultureInfo("pt-BR"));

        var network = service.DescribeException(new HttpRequestException("socket 10.0.0.1 failed"));
        var security = service.DescribeException(new InvalidDataException("SHA mismatch"));
        var unexpected = service.DescribeException(new InvalidOperationException("internal detail"));

        Assert.Equal("Não foi possível conectar agora. Verifique a internet e tente novamente.", network);
        Assert.Equal("Não foi possível verificar as informações recebidas com segurança. Nada foi alterado.", security);
        Assert.DoesNotContain("internal detail", unexpected, StringComparison.Ordinal);
        Assert.Contains("envie um relato de bug", unexpected, StringComparison.Ordinal);
    }

    [Fact]
    public void EverySupportedCatalog_HasExactlyTheSameKeys()
    {
        var manager = new ResourceManager(
            "Ralven.App.Resources.Strings",
            typeof(LocalizationService).Assembly);
        using var english = manager.GetResourceSet(
            CultureInfo.GetCultureInfo("en-US"),
            createIfNotExists: true,
            tryParents: true);
        Assert.NotNull(english);
        var englishKeys = KeysOf(english!);
        Assert.True(englishKeys.Count >= 100);
        foreach (var language in LocalizationCatalog.SupportedLanguages.Where(language => language.CultureName != AppLanguage.English))
        {
            using var localized = manager.GetResourceSet(
                CultureInfo.GetCultureInfo(language.CultureName),
                createIfNotExists: true,
                tryParents: true);

            Assert.NotNull(localized);
            Assert.Equal(englishKeys, KeysOf(localized!));
        }
    }

    [Fact]
    public void SetLanguage_Spanish_UpdatesCultureAndPreference()
    {
        var service = new LocalizationService(CultureInfo.GetCultureInfo("en-US"));

        service.SetLanguage(AppLanguage.Spanish);

        Assert.Equal(AppLanguage.Spanish, service.CurrentLanguage);
        Assert.Equal(AppLanguage.Spanish, service.CurrentPreference);
        Assert.Equal("es-ES", service.CurrentCulture.Name);
        Assert.Equal("Configuración", service["Settings.Title"]);
    }

    [Fact]
    public void PseudoLocalization_ExpandsTextAndPreservesPlaceholders()
    {
        var service = new LocalizationService(CultureInfo.GetCultureInfo("en-US"));

        service.SetLanguage(LocalizationCatalog.PseudoCultureName);
        var value = service.Format("About.VersionDeveloper", "0.2.0");

        Assert.StartsWith("⟦", value, StringComparison.Ordinal);
        Assert.EndsWith("⟧", value, StringComparison.Ordinal);
        Assert.Contains("0.2.0", value, StringComparison.Ordinal);
        Assert.True(value.Length > "Version 0.2.0  •  Ralven".Length);
    }

    [Fact]
    public void ExistingSettingsJson_DefaultsToAutomaticLanguage()
    {
        const string previousVersionJson = "{\"theme\":\"dark\"}";

        var settings = JsonSerializer.Deserialize<AppSettings>(
            previousVersionJson,
            RalvenJson.Options);

        Assert.NotNull(settings);
        Assert.Equal(AppLanguagePreference.Automatic, settings!.Language);
        Assert.Equal(AppThemePreference.Dark, settings.Theme);
        Assert.True(settings.ShareAnonymousTelemetry);
    }

    [Fact]
    public void LanguagePreference_RoundTripsThroughSettingsJson()
    {
        var source = new AppSettings
        {
            Language = AppLanguagePreference.PortugueseBrazil,
            Theme = AppThemePreference.System
        };

        var json = JsonSerializer.Serialize(source, RalvenJson.Options);
        var result = JsonSerializer.Deserialize<AppSettings>(json, RalvenJson.Options);

        Assert.Contains("\"language\":\"portugueseBrazil\"", json, StringComparison.Ordinal);
        Assert.NotNull(result);
        Assert.Equal(AppLanguagePreference.PortugueseBrazil, result!.Language);
    }

    [Fact]
    public void SpanishLanguagePreference_RoundTripsThroughSettingsJson()
    {
        var source = new AppSettings
        {
            Language = AppLanguagePreference.Spanish,
            Theme = AppThemePreference.System
        };

        var json = JsonSerializer.Serialize(source, RalvenJson.Options);
        var result = JsonSerializer.Deserialize<AppSettings>(json, RalvenJson.Options);

        Assert.Contains("\"language\":\"spanish\"", json, StringComparison.Ordinal);
        Assert.NotNull(result);
        Assert.Equal(AppLanguagePreference.Spanish, result!.Language);
    }

    [Fact]
    public void FrenchLanguagePreference_RoundTripsThroughSettingsJson()
    {
        var source = new AppSettings { Language = "fr-FR" };

        var json = JsonSerializer.Serialize(source, RalvenJson.Options);
        var result = JsonSerializer.Deserialize<AppSettings>(json, RalvenJson.Options);

        Assert.Contains("\"language\":\"fr-FR\"", json, StringComparison.Ordinal);
        Assert.Equal("fr-FR", result!.Language);
    }

    private static SortedSet<string> KeysOf(ResourceSet resourceSet)
    {
        return resourceSet.Cast<DictionaryEntry>()
            .Select(entry => Assert.IsType<string>(entry.Key))
            .ToSortedSet(StringComparer.Ordinal);
    }
}
