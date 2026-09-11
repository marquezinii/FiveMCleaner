using System.Text.Json;
using Ralven.App.Services;
using Ralven.Contracts;
using Xunit;

namespace Ralven.Tests.App;

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
        Assert.Contains("inicialização saudável", entry.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentVersion_Is9()
    {
        Assert.Equal(9, PrivacyConsentPolicy.CurrentVersion);
    }

    [Fact]
    public void History_ContainsAllVersions1ThroughCurrentVersion()
    {
        for (var version = 1; version <= PrivacyConsentPolicy.CurrentVersion; version++)
        {
            var entry = Assert.Single(PrivacyConsentPolicy.History, e => e.Version == version);
            Assert.False(string.IsNullOrWhiteSpace(entry.Summary));
        }
    }

    [Fact]
    public void RequiresRenewal_Version4ToVersion5_ReturnsTrue()
    {
        Assert.True(PrivacyConsentPolicy.RequiresRenewal(4));
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
        Assert.False(decision.AreEssentialDiagnosticsAuthorized);
        Assert.False(decision.AreOptionalReportsAuthorized);
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
        Assert.False(decision.AreEssentialDiagnosticsAuthorized);
        Assert.False(decision.AreOptionalReportsAuthorized);
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
        Assert.False(decision.AreEssentialDiagnosticsAuthorized);
        Assert.False(decision.AreOptionalReportsAuthorized);
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
        Assert.True(decision.AreEssentialDiagnosticsAuthorized);
        Assert.True(decision.AreOptionalReportsAuthorized);
    }

    [Fact]
    public void Evaluate_UserDeclinedOptionalReportsAtCurrentVersion_StillAuthorizesEssentialDiagnostics()
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
        Assert.True(decision.AreEssentialDiagnosticsAuthorized);
        Assert.False(decision.AreOptionalReportsAuthorized);
    }

    [Fact]
    public void Evaluate_LegacyMixedPreferencesAtCurrentVersion_PreservesTheOptOut()
    {
        var settings = new AppSettings
        {
            ShareAnonymousTelemetry = true,
            ShareCrashReports = false,
            PrivacyConsentVersion = PrivacyConsentPolicy.CurrentVersion
        };

        var decision = PrivacyConsentEvaluator.Evaluate(settings, settingsFileExistedBeforeLoad: true);

        Assert.False(decision.RequiresConsentScreen);
        Assert.True(decision.AreEssentialDiagnosticsAuthorized);
        Assert.False(decision.AreOptionalReportsAuthorized);
    }

    [Fact]
    public void Evaluate_InverseLegacyMixedPreferencesAtCurrentVersion_PreservesTheOptOut()
    {
        var settings = new AppSettings
        {
            ShareAnonymousTelemetry = false,
            ShareCrashReports = true,
            PrivacyConsentVersion = PrivacyConsentPolicy.CurrentVersion
        };

        var decision = PrivacyConsentEvaluator.Evaluate(settings, settingsFileExistedBeforeLoad: true);

        Assert.False(decision.RequiresConsentScreen);
        Assert.True(decision.AreEssentialDiagnosticsAuthorized);
        Assert.False(decision.AreOptionalReportsAuthorized);
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
        Assert.False(decision.AreEssentialDiagnosticsAuthorized);
        Assert.False(decision.AreOptionalReportsAuthorized);
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

        Assert.False(decision.AreEssentialDiagnosticsAuthorized);
        Assert.False(decision.AreOptionalReportsAuthorized);
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

        Assert.False(decision.AreEssentialDiagnosticsAuthorized);
        Assert.False(decision.AreOptionalReportsAuthorized);
    }
}

public sealed class AppSettingsSerializationTests
{
    // Mirrors the real serializer used to persist settings.json
    // (AppOptimizationService.indentedJson is RalvenJson.Options with
    // WriteIndented=true), including camelCase property names and the
    // string-enum converter.
    private static readonly JsonSerializerOptions Options = RalvenJson.Options;

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
        Assert.False(settings.StartMinimized);
        Assert.True(settings.NotifyWhenUpdateAvailable);
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
    public void Deserialize_JsonWithoutShareAnonymousTelemetry_DefaultsToTrue()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}", Options)!;

        Assert.True(settings.ShareAnonymousTelemetry);
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

    /// <summary>
    /// "Minimizar para a bandeja" must come pre-enabled for every fresh
    /// install and for every upgrade from a version old enough to predate
    /// this field, exactly like the telemetry/crash-report toggles above —
    /// there is no installer step that writes this value, so the record's
    /// own default initializer is the only thing guaranteeing it. Locked
    /// down with a dedicated test per explicit request, since a silent
    /// regression here (falling back to the CLR default of <see langword="false"/>
    /// instead of the intended <see langword="true"/>) would be easy to miss.
    /// </summary>
    [Fact]
    public void Deserialize_JsonWithoutMinimizeToTrayOnClose_DefaultsToTrue()
    {
        const string json = "{}";

        var settings = JsonSerializer.Deserialize<AppSettings>(json, Options)!;

        Assert.True(settings.MinimizeToTrayOnClose);
    }

    [Fact]
    public void Deserialize_JsonWithoutGeneralPreferences_UsesFirstInstallDefaults()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}", Options)!;

        Assert.True(settings.LaunchAtStartup);
        Assert.False(settings.StartMinimized);
        Assert.True(settings.CheckForUpdates);
        Assert.True(settings.NotifyWhenUpdateAvailable);
    }

    [Fact]
    public void Deserialize_OldInstallationThatExplicitlyDisabledMinimizeToTray_PreservesTheChoice()
    {
        const string json = """{ "minimizeToTrayOnClose": false }""";

        var settings = JsonSerializer.Deserialize<AppSettings>(json, Options)!;

        Assert.False(settings.MinimizeToTrayOnClose);
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
            StartMinimized = false,
            CheckForUpdates = false,
            NotifyWhenUpdateAvailable = false,
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

public sealed class PrivacyConsentOutcomeBuilderTests
{
    private static AppSettings SettingsWithDistinctPreferences() => new()
    {
        Language = AppLanguagePreference.English,
        Theme = AppThemePreference.Dark,
        MinimizeToTrayOnClose = false,
        LaunchAtStartup = true,
        StartMinimized = false,
        CheckForUpdates = false,
        NotifyWhenUpdateAvailable = false,
        ShareAnonymousTelemetry = true,
        ShareCrashReports = true,
        PrivacyConsentVersion = null
    };

    [Fact]
    public void BuildConfirmed_OptionalReportsAccepted_SetsBothCompatibilityFieldsTrue()
    {
        var result = PrivacyConsentOutcomeBuilder.BuildConfirmed(
            SettingsWithDistinctPreferences(),
            acceptOptionalReports: true);

        Assert.True(result.ShareAnonymousTelemetry);
        Assert.True(result.ShareCrashReports);
        Assert.Equal(PrivacyConsentPolicy.CurrentVersion, result.PrivacyConsentVersion);
    }

    [Fact]
    public void BuildConfirmed_OptionalReportsDeclined_SetsBothCompatibilityFieldsFalse()
    {
        var result = PrivacyConsentOutcomeBuilder.BuildConfirmed(
            SettingsWithDistinctPreferences(),
            acceptOptionalReports: false);

        Assert.False(result.ShareAnonymousTelemetry);
        Assert.False(result.ShareCrashReports);
        Assert.Equal(PrivacyConsentPolicy.CurrentVersion, result.PrivacyConsentVersion);
    }

    [Fact]
    public void BuildConfirmed_PreservesEveryOtherExistingSetting()
    {
        var current = SettingsWithDistinctPreferences();

        var result = PrivacyConsentOutcomeBuilder.BuildConfirmed(current, true);

        Assert.Equal(current.Language, result.Language);
        Assert.Equal(current.Theme, result.Theme);
        Assert.Equal(current.MinimizeToTrayOnClose, result.MinimizeToTrayOnClose);
        Assert.Equal(current.LaunchAtStartup, result.LaunchAtStartup);
        Assert.Equal(current.StartMinimized, result.StartMinimized);
        Assert.Equal(current.CheckForUpdates, result.CheckForUpdates);
        Assert.Equal(current.NotifyWhenUpdateAvailable, result.NotifyWhenUpdateAvailable);
    }

}
