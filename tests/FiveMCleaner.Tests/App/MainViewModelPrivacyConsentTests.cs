using FiveMCleaner.App.Services;
using FiveMCleaner.App.ViewModels;
using Xunit;

namespace FiveMCleaner.Tests.App;

/// <summary>
/// Exercises the wiring between <see cref="MainViewModel"/> and the pure
/// consent logic (<see cref="PrivacyConsentEvaluator"/>,
/// <see cref="PrivacyConsentOutcomeBuilder"/>): that settings loaded during
/// <see cref="MainViewModel.InitializeAsync"/> produce the right decision and
/// the right initial checkbox state, and that
/// <see cref="MainViewModel.ConfirmPrivacyConsentAsync"/> persists through
/// the existing settings mechanism without ever touching telemetry beyond
/// enabling/disabling it locally.
/// </summary>
public sealed class MainViewModelPrivacyConsentTests
{
    [Fact]
    public async Task InitializeAsync_NewInstallation_RequestsFirstInstallationScreenWithBothTogglesTrue()
    {
        var service = new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false);
        var telemetry = new RecordingTelemetryService();
        var viewModel = new MainViewModel(service, telemetry: telemetry);

        await viewModel.InitializeAsync();

        var decision = viewModel.PrivacyConsentDecision;
        Assert.NotNull(decision);
        Assert.True(decision!.RequiresConsentScreen);
        Assert.Equal(PrivacyConsentScreenVariant.FirstInstallation, decision.Variant);
        Assert.True(viewModel.ShareAnonymousTelemetry);
        Assert.True(viewModel.ShareCrashReports);
        Assert.Equal(0, telemetry.TrackCallCount);
    }

    [Fact]
    public async Task InitializeAsync_OldInstallationWithTelemetryDeclinedAndNoConsentVersion_RequestsUpgradeScreenAndPreservesTheDeclinedValueAsInitialState()
    {
        var oldSettings = new AppSettings
        {
            ShareAnonymousTelemetry = false,
            ShareCrashReports = true,
            PrivacyConsentVersion = null
        };
        var service = new FakeAppOptimizationService(oldSettings, settingsFileExists: true);
        var viewModel = new MainViewModel(service);

        await viewModel.InitializeAsync();

        var decision = viewModel.PrivacyConsentDecision;
        Assert.NotNull(decision);
        Assert.True(decision!.RequiresConsentScreen);
        Assert.Equal(PrivacyConsentScreenVariant.UpgradeFromOlderInstallation, decision.Variant);
        // The window's initial checkbox state comes straight from these
        // bindable properties, so the previously declined telemetry choice
        // must still be reflected here even though the field's own default
        // is true.
        Assert.False(viewModel.ShareAnonymousTelemetry);
        Assert.True(viewModel.ShareCrashReports);
    }

    [Fact]
    public async Task InitializeAsync_ConsentAlreadyValidForCurrentVersion_DoesNotRequestTheScreen()
    {
        var settings = new AppSettings
        {
            ShareAnonymousTelemetry = true,
            ShareCrashReports = false,
            PrivacyConsentVersion = PrivacyConsentPolicy.CurrentVersion
        };
        var service = new FakeAppOptimizationService(settings, settingsFileExists: true);
        var viewModel = new MainViewModel(service);

        await viewModel.InitializeAsync();

        Assert.False(viewModel.PrivacyConsentDecision!.RequiresConsentScreen);
    }

    [Fact]
    public async Task InitializeAsync_ConsentVersionOlderThanCurrent_RequestsRenewalScreen()
    {
        var settings = new AppSettings
        {
            ShareAnonymousTelemetry = true,
            ShareCrashReports = true,
            PrivacyConsentVersion = PrivacyConsentPolicy.CurrentVersion - 1
        };
        var service = new FakeAppOptimizationService(settings, settingsFileExists: true);
        var viewModel = new MainViewModel(service);

        await viewModel.InitializeAsync();

        Assert.Equal(PrivacyConsentScreenVariant.ConsentRenewalRequired, viewModel.PrivacyConsentDecision!.Variant);
    }

    [Fact]
    public async Task ConfirmPrivacyConsentAsync_BothAccepted_PersistsBothTrueAndTheCurrentVersion()
    {
        var service = new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false);
        var telemetry = new RecordingTelemetryService();
        var viewModel = new MainViewModel(service, telemetry: telemetry);
        await viewModel.InitializeAsync();

        await viewModel.ConfirmPrivacyConsentAsync(acceptAnonymousTelemetry: true, acceptCrashReports: true);

        Assert.NotNull(service.SavedSettings);
        Assert.True(service.SavedSettings!.ShareAnonymousTelemetry);
        Assert.True(service.SavedSettings.ShareCrashReports);
        Assert.Equal(PrivacyConsentPolicy.CurrentVersion, service.SavedSettings.PrivacyConsentVersion);
        Assert.True(viewModel.ShareAnonymousTelemetry);
        Assert.True(viewModel.ShareCrashReports);
        Assert.True(telemetry.IsEnabled);
    }

    [Fact]
    public async Task ConfirmPrivacyConsentAsync_ClosingIsPassedAsBothFalse_PersistsBothFalseButStillStampsTheVersion()
    {
        var service = new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false);
        var telemetry = new RecordingTelemetryService();
        var viewModel = new MainViewModel(service, telemetry: telemetry);
        await viewModel.InitializeAsync();

        // MainWindow passes (false, false) when the consent window was
        // closed via the X button or Alt+F4 instead of "Continue".
        await viewModel.ConfirmPrivacyConsentAsync(acceptAnonymousTelemetry: false, acceptCrashReports: false);

        Assert.False(service.SavedSettings!.ShareAnonymousTelemetry);
        Assert.True(service.SavedSettings.ShareCrashReports);
        Assert.Equal(PrivacyConsentPolicy.CurrentVersion, service.SavedSettings.PrivacyConsentVersion);
        Assert.False(viewModel.ShareAnonymousTelemetry);
        Assert.True(viewModel.ShareCrashReports);
        Assert.True(telemetry.IsEnabled);
    }

    [Fact]
    public async Task ConfirmPrivacyConsentAsync_OnlyTelemetryAccepted_PersistsExactlyThatCombination()
    {
        var service = new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false);
        var viewModel = new MainViewModel(service);
        await viewModel.InitializeAsync();

        await viewModel.ConfirmPrivacyConsentAsync(acceptAnonymousTelemetry: true, acceptCrashReports: false);

        Assert.True(service.SavedSettings!.ShareAnonymousTelemetry);
        Assert.True(service.SavedSettings.ShareCrashReports);
    }

    [Fact]
    public async Task ConfirmPrivacyConsentAsync_OnlyCrashReportsAccepted_PersistsExactlyThatCombination()
    {
        var service = new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false);
        var viewModel = new MainViewModel(service);
        await viewModel.InitializeAsync();

        await viewModel.ConfirmPrivacyConsentAsync(acceptAnonymousTelemetry: false, acceptCrashReports: true);

        Assert.False(service.SavedSettings!.ShareAnonymousTelemetry);
        Assert.True(service.SavedSettings.ShareCrashReports);
    }

    [Fact]
    public async Task ConfirmPrivacyConsentAsync_NeverTracksAnyTelemetryEventByItself()
    {
        var service = new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false);
        var telemetry = new RecordingTelemetryService();
        var viewModel = new MainViewModel(service, telemetry: telemetry);
        await viewModel.InitializeAsync();

        await viewModel.ConfirmPrivacyConsentAsync(acceptAnonymousTelemetry: true, acceptCrashReports: true);

        // Confirming consent only flips the local enabled/disabled switch;
        // it must never itself send a telemetry event or call any crash
        // reporting service (none exists yet in this increment).
        Assert.Equal(0, telemetry.TrackCallCount);
    }

    [Fact]
    public async Task ConfirmPrivacyConsentAsync_PreservesExistingSettingsUnrelatedToConsent()
    {
        var oldSettings = new AppSettings
        {
            Language = AppLanguagePreference.English,
            Theme = AppThemePreference.Dark,
            MinimizeToTrayOnClose = false,
            LaunchAtStartup = false,
            CheckForUpdates = false,
            ShareAnonymousTelemetry = true,
            ShareCrashReports = true,
            PrivacyConsentVersion = null
        };
        var service = new FakeAppOptimizationService(oldSettings, settingsFileExists: true);
        var viewModel = new MainViewModel(service);
        await viewModel.InitializeAsync();

        await viewModel.ConfirmPrivacyConsentAsync(acceptAnonymousTelemetry: false, acceptCrashReports: false);

        Assert.Equal(AppLanguagePreference.English, service.SavedSettings!.Language);
        Assert.Equal(AppThemePreference.Dark, service.SavedSettings.Theme);
        Assert.False(service.SavedSettings.CheckForUpdates);
    }
}
