using System.Text.Json;
using Ralven.App.Services;
using Ralven.Contracts;
using Xunit;

namespace Ralven.Tests.App;

public sealed class AppSettingsLenientReadTests
{
    [Fact]
    public async Task SaveAndLoadSettingsAsync_RoundTripsEveryUserPreference()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var service = new AppOptimizationService(temporaryDirectory.Path);
        var expected = new AppSettings
        {
            Language = AppLanguagePreference.Spanish,
            Theme = AppThemePreference.Light,
            MinimizeToTrayOnClose = false,
            LaunchAtStartup = false,
            StartMinimized = true,
            CheckForUpdates = false,
            NotifyWhenUpdateAvailable = false,
            ShareAnonymousTelemetry = false,
            ShareCrashReports = false,
            PrivacyConsentVersion = 3,
            DismissedLiveAlertId = "alert-42",
            LastSeenReleaseNotesVersion = "1.2.3"
        };

        await service.SaveSettingsAsync(expected);

        Assert.True(service.SettingsFileExists());
        Assert.Equal(expected, await service.LoadSettingsAsync());
    }

    // Under the old strict read (RalvenJson.Options:
    // UnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive=false,
    // comments disallowed) any schema drift in settings.json threw a
    // JsonException, LoadSettingsAsync returned a fresh AppSettings, and the
    // user's declined telemetry toggles reverted to their default (true) with
    // a null consent version -- re-arming the consent screen and silently
    // flipping a deliberate opt-out. These tests lock the lenient-read
    // contract in AppOptimizationService.DeserializeSettings.

    [Fact]
    public void DeserializeSettings_UnknownMembersFromANewerBuild_AreIgnoredAndKnownValuesPreserved()
    {
        const string json = """
        {
          "language": "english",
          "minimizeToTrayOnClose": true,
          "shareAnonymousTelemetry": false,
          "shareCrashReports": false,
          "privacyConsentVersion": 3,
          "someFutureSetting": 42,
          "anotherFutureSetting": { "nested": true }
        }
        """;

        var settings = AppOptimizationService.DeserializeSettings(json);

        Assert.False(settings.ShareAnonymousTelemetry);
        Assert.False(settings.ShareCrashReports);
        Assert.Equal(3, settings.PrivacyConsentVersion);
        Assert.Equal(AppLanguagePreference.English, settings.Language);
        Assert.True(settings.MinimizeToTrayOnClose);
    }

    [Fact]
    public void DeserializeSettings_KeysWithDifferentCasing_AreStillMatched()
    {
        const string json = """
        {
          "ShareAnonymousTelemetry": false,
          "shareCrashReports": false,
          "PRIVACYCONSENTVERSION": 3
        }
        """;

        var settings = AppOptimizationService.DeserializeSettings(json);

        Assert.False(settings.ShareAnonymousTelemetry);
        Assert.False(settings.ShareCrashReports);
        Assert.Equal(3, settings.PrivacyConsentVersion);
    }

    [Fact]
    public void DeserializeSettings_CommentsInTheFile_AreTolerated()
    {
        const string json = """
        {
          // usuario editou manualmente
          "shareAnonymousTelemetry": false,
          /* bloco */
          "shareCrashReports": false
        }
        """;

        var settings = AppOptimizationService.DeserializeSettings(json);

        Assert.False(settings.ShareAnonymousTelemetry);
        Assert.False(settings.ShareCrashReports);
    }

    [Fact]
    public void DeserializeSettings_GenuinelyCorruptJson_StillThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => AppOptimizationService.DeserializeSettings("{ not json"));
    }

    [Fact]
    public void DeserializeSettings_WellFormedJson_AgreesWithTheStrictOptions()
    {
        const string json = """
        { "shareAnonymousTelemetry": false, "shareCrashReports": false, "privacyConsentVersion": 3 }
        """;

        var lenient = AppOptimizationService.DeserializeSettings(json);
        var strict = JsonSerializer.Deserialize<AppSettings>(json, RalvenJson.Options)!;

        Assert.Equal(strict, lenient);
    }
}
