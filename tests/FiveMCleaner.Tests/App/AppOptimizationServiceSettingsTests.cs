using System.Text.Json;
using FiveMCleaner.App.Services;
using FiveMCleaner.Contracts;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class AppSettingsLenientReadTests
{
    // Under the old strict read (FiveMCleanerJson.Options:
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
        var strict = JsonSerializer.Deserialize<AppSettings>(json, FiveMCleanerJson.Options)!;

        Assert.Equal(strict, lenient);
    }
}
