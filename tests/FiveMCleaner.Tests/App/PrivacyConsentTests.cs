using System.Text.Json;
using FiveMCleaner.App.Services;
using FiveMCleaner.Contracts;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class PrivacyConsentPolicyTests
{
    [Fact]
    public void RequiresRenewal_IsTrueWhenStoredVersionIsNull()
    {
        Assert.True(PrivacyConsentPolicy.RequiresRenewal(null));
    }

    [Fact]
    public void RequiresRenewal_IsTrueWhenStoredVersionIsOlderThanCurrent()
    {
        Assert.True(PrivacyConsentPolicy.RequiresRenewal(PrivacyConsentPolicy.CurrentVersion - 1));
    }

    [Fact]
    public void RequiresRenewal_IsFalseWhenStoredVersionMatchesCurrent()
    {
        Assert.False(PrivacyConsentPolicy.RequiresRenewal(PrivacyConsentPolicy.CurrentVersion));
    }

    [Fact]
    public void History_ContainsAnEntryForTheCurrentVersionWithASummary()
    {
        var entry = Assert.Single(PrivacyConsentPolicy.History, e => e.Version == PrivacyConsentPolicy.CurrentVersion);
        Assert.False(string.IsNullOrWhiteSpace(entry.Summary));
    }
}

public sealed class PrivacyConsentEvaluatorTests
{
    [Fact]
    public void Evaluate_NewInstallationWithoutSettingsFile_RequiresFirstInstallationScreen()
    {
        var settings = new AppSettings();

        var decision = PrivacyConsentEvaluator.Evaluate(settings, settingsFileExistedBeforeLoad: false);

        Assert.True(decision.RequiresConsentScreen);
        Assert.Equal(PrivacyConsentScreenVariant.FirstInstallation, decision.Variant);
        Assert.False(decision.IsAnonymousTelemetryAuthorized);
        Assert.False(decision.IsCrashReportingAuthorized);
    }

    [Fact]
    public void Evaluate_OldSettingsFileWithOnlyTelemetryAcceptedAndNoConsentVersion_RequiresUpgradeScreen()
    {
        var settings = new AppSettings
        {
            ShareAnonymousTelemetry = true,
            ShareCrashReports = true,
            PrivacyConsentVersion = null
        };

        var decision = PrivacyConsentEvaluator.Evaluate(settings, settingsFileExistedBeforeLoad: true);

        Assert.True(decision.RequiresConsentScreen);
        Assert.Equal(PrivacyConsentScreenVariant.UpgradeFromOlderInstallation, decision.Variant);
        Assert.False(decision.IsAnonymousTelemetryAuthorized);
        Assert.False(decision.IsCrashReportingAuthorized);
    }

    [Fact]
    public void Evaluate_OldSettingsFileWithTelemetryDeclinedAndNoConsentVersion_RequiresUpgradeScreenAndAuthorizesNothing()
    {
        var settings = new AppSettings
        {
            ShareAnonymousTelemetry = false,
            ShareCrashReports = true,
            PrivacyConsentVersion = null
        };

        var decision = PrivacyConsentEvaluator.Evaluate(settings, settingsFileExistedBeforeLoad: true);

        Assert.True(decision.RequiresConsentScreen);
        Assert.Equal(PrivacyConsentScreenVariant.UpgradeFromOlderInstallation, decision.Variant);
        Assert.False(decision.IsAnonymousTelemetryAuthorized);
        Assert.False(decision.IsCrashReportingAuthorized);
    }

    [Fact]
    public void Evaluate_UserAcceptedBothAtCurrentVersion_IsAlreadyValidAndAuthorizesBoth()
    {
        var settings = new AppSettings
        {
            ShareAnonymousTelemetry = true,
            ShareCrashReports = true,
            PrivacyConsentVersion = PrivacyConsentPolicy.CurrentVersion
        };

        var decision = PrivacyConsentEvaluator.Evaluate(settings, settingsFileExistedBeforeLoad: true);

        Assert.False(decision.RequiresConsentScreen);
        Assert.Equal(PrivacyConsentScreenVariant.AlreadyValid, decision.Variant);
        Assert.True(decision.IsAnonymousTelemetryAuthorized);
        Assert.True(decision.IsCrashReportingAuthorized);
    }

    [Fact]
    public void Evaluate_UserDeclinedBothAtCurrentVersion_IsAlreadyValidButAuthorizesNothing()
    {
        var settings = new AppSettings
        {
            ShareAnonymousTelemetry = false,
            ShareCrashReports = false,
            PrivacyConsentVersion = PrivacyConsentPolicy.CurrentVersion
        };

        var decision = PrivacyConsentEvaluator.Evaluate(settings, settingsFileExistedBeforeLoad: true);

        Assert.False(decision.RequiresConsentScreen);
        Assert.Equal(PrivacyConsentScreenVariant.AlreadyValid, decision.Variant);
        Assert.False(decision.IsAnonymousTelemetryAuthorized);
        Assert.False(decision.IsCrashReportingAuthorized);
    }

    [Fact]
    public void Evaluate_UserAcceptedOnlyTelemetryAtCurrentVersion_AuthorizesOnlyTelemetry()
    {
        var settings = new AppSettings
        {
            ShareAnonymousTelemetry = true,
            ShareCrashReports = false,
            PrivacyConsentVersion = PrivacyConsentPolicy.CurrentVersion
        };

        var decision = PrivacyConsentEvaluator.Evaluate(settings, settingsFileExistedBeforeLoad: true);

        Assert.False(decision.RequiresConsentScreen);
        Assert.True(decision.IsAnonymousTelemetryAuthorized);
        Assert.False(decision.IsCrashReportingAuthorized);
    }

    [Fact]
    public void Evaluate_UserAcceptedOnlyCrashReportsAtCurrentVersion_AuthorizesOnlyCrashReports()
    {
        var settings = new AppSettings
        {
            ShareAnonymousTelemetry = false,
            ShareCrashReports = true,
            PrivacyConsentVersion = PrivacyConsentPolicy.CurrentVersion
        };

        var decision = PrivacyConsentEvaluator.Evaluate(settings, settingsFileExistedBeforeLoad: true);

        Assert.False(decision.RequiresConsentScreen);
        Assert.False(decision.IsAnonymousTelemetryAuthorized);
        Assert.True(decision.IsCrashReportingAuthorized);
    }

    [Fact]
    public void Evaluate_ConsentVersionOlderThanCurrent_RequiresRenewalScreenAndAuthorizesNothing()
    {
        var settings = new AppSettings
        {
            ShareAnonymousTelemetry = true,
            ShareCrashReports = true,
            PrivacyConsentVersion = PrivacyConsentPolicy.CurrentVersion - 1
        };

        var decision = PrivacyConsentEvaluator.Evaluate(settings, settingsFileExistedBeforeLoad: true);

        Assert.True(decision.RequiresConsentScreen);
        Assert.Equal(PrivacyConsentScreenVariant.ConsentRenewalRequired, decision.Variant);
        Assert.False(decision.IsAnonymousTelemetryAuthorized);
        Assert.False(decision.IsCrashReportingAuthorized);
    }

    [Fact]
    public void Evaluate_ConsentVersionEqualToCurrent_DoesNotRequireScreen()
    {
        var settings = new AppSettings
        {
            ShareAnonymousTelemetry = true,
            ShareCrashReports = true,
            PrivacyConsentVersion = PrivacyConsentPolicy.CurrentVersion
        };

        var decision = PrivacyConsentEvaluator.Evaluate(settings, settingsFileExistedBeforeLoad: true);

        Assert.False(decision.RequiresConsentScreen);
        Assert.Equal(PrivacyConsentScreenVariant.AlreadyValid, decision.Variant);
    }

    [Fact]
    public void Evaluate_NullVersionNeverAuthorizesAnythingEvenWhenBothTogglesAreTrue()
    {
        var settings = new AppSettings { ShareAnonymousTelemetry = true, ShareCrashReports = true, PrivacyConsentVersion = null };

        var decision = PrivacyConsentEvaluator.Evaluate(settings, settingsFileExistedBeforeLoad: false);

        Assert.False(decision.IsAnonymousTelemetryAuthorized);
        Assert.False(decision.IsCrashReportingAuthorized);
    }

    [Fact]
    public void Evaluate_StaleVersionNeverAuthorizesCrashReportingEvenWhenToggleIsTrue()
    {
        var settings = new AppSettings
        {
            ShareAnonymousTelemetry = true,
            ShareCrashReports = true,
            PrivacyConsentVersion = PrivacyConsentPolicy.CurrentVersion - 1
        };

        var decision = PrivacyConsentEvaluator.Evaluate(settings, settingsFileExistedBeforeLoad: true);

        Assert.False(decision.IsAnonymousTelemetryAuthorized);
        Assert.False(decision.IsCrashReportingAuthorized);
    }
}

public sealed class AppSettingsSerializationTests
{
    // Mirrors the real serializer used to persist settings.json
    // (AppOptimizationService.indentedJson is FiveMCleanerJson.Options with
    // WriteIndented=true), including camelCase property names and the
    // string-enum converter.
    private static readonly JsonSerializerOptions Options = FiveMCleanerJson.Options;

    [Fact]
    public void Deserialize_OldJsonWithOnlyAnonymousTelemetryTrue_PreservesValueAndDefaultsNewFieldsSafely()
    {
        const string json = """
        {
          "language": "automatic",
          "theme": "system",
          "minimizeToTrayOnClose": true,
          "launchAtStartup": false,
          "checkForUpdates": true,
          "shareAnonymousTelemetry": true
        }
        """;

        var settings = JsonSerializer.Deserialize<AppSettings>(json, Options)!;

        Assert.True(settings.ShareAnonymousTelemetry);
        Assert.True(settings.ShareCrashReports);
        Assert.Null(settings.PrivacyConsentVersion);
    }

    [Fact]
    public void Deserialize_OldJsonWithOnlyAnonymousTelemetryFalse_PreservesTheDeclinedValue()
    {
        const string json = """
        {
          "minimizeToTrayOnClose": true,
          "shareAnonymousTelemetry": false
        }
        """;

        var settings = JsonSerializer.Deserialize<AppSettings>(json, Options)!;

        Assert.False(settings.ShareAnonymousTelemetry);
        Assert.True(settings.ShareCrashReports);
        Assert.Null(settings.PrivacyConsentVersion);
        Assert.True(settings.MinimizeToTrayOnClose);
    }

    [Fact]
    public void Deserialize_JsonWithoutShareCrashReports_DefaultsToTrue()
    {
        const string json = "{}";

        var settings = JsonSerializer.Deserialize<AppSettings>(json, Options)!;

        Assert.True(settings.ShareCrashReports);
    }

    [Fact]
    public void Deserialize_JsonWithoutPrivacyConsentVersion_DefaultsToNull()
    {
        const string json = "{}";

        var settings = JsonSerializer.Deserialize<AppSettings>(json, Options)!;

        Assert.Null(settings.PrivacyConsentVersion);
    }

    [Fact]
    public void RoundTrip_SerializeThenDeserialize_PreservesAllFieldsIncludingNewOnes()
    {
        var original = new AppSettings
        {
            Language = AppLanguagePreference.English,
            Theme = AppThemePreference.Dark,
            MinimizeToTrayOnClose = false,
            LaunchAtStartup = true,
            CheckForUpdates = false,
            ShareAnonymousTelemetry = false,
            ShareCrashReports = false,
            PrivacyConsentVersion = 3
        };

        var json = JsonSerializer.Serialize(original, Options);
        var roundTripped = JsonSerializer.Deserialize<AppSettings>(json, Options);

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void RoundTrip_WithNullPrivacyConsentVersion_StaysNull()
    {
        var original = new AppSettings { PrivacyConsentVersion = null };

        var json = JsonSerializer.Serialize(original, Options);
        var roundTripped = JsonSerializer.Deserialize<AppSettings>(json, Options);

        Assert.Null(roundTripped!.PrivacyConsentVersion);
    }
}
