using Ralven.App.Services;
using Ralven.App.ViewModels;
using Xunit;

namespace Ralven.Tests.App;

public sealed class MainViewModelSettingsTests
{
    [Fact]
    public async Task InitializeAsync_OldSettingsDefaultToVisibleStartup()
    {
        var settings = new AppSettings
        {
            MinimizeToTrayOnClose = true,
            StartMinimized = null
        };
        var viewModel = CreateViewModel(settings);

        await viewModel.InitializeAsync();

        Assert.False(viewModel.StartMinimized);
    }

    [Fact]
    public void NewSettings_UseRequestedGeneralDefaults()
    {
        var settings = new AppSettings();

        Assert.True(settings.LaunchAtStartup);
        Assert.True(settings.MinimizeToTrayOnClose);
        Assert.False(settings.StartMinimized);
        Assert.True(settings.CheckForUpdates);
        Assert.True(settings.NotifyWhenUpdateAvailable);
    }

    [Fact]
    public async Task ConfirmPrivacyConsentAsync_PreservesNewGeneralPreferences()
    {
        var service = new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false);
        var viewModel = new MainViewModel(
            service,
            startupRegistration: new SessionStartupRegistrationService());
        await viewModel.InitializeAsync();
        viewModel.StartMinimized = false;
        viewModel.CheckForUpdates = false;
        viewModel.NotifyWhenUpdateAvailable = false;

        await viewModel.ConfirmPrivacyConsentAsync(viewModel.ShareOptionalReports);

        Assert.False(service.SavedSettings!.StartMinimized);
        Assert.False(service.SavedSettings.CheckForUpdates);
        Assert.False(service.SavedSettings.NotifyWhenUpdateAvailable);
    }

    [Fact]
    public async Task RestoreGeneralSettingsDefaultsAsync_RestoresGeneralPreferencesAndPreservesPrivateState()
    {
        var initialSettings = new AppSettings
        {
            Language = AppLanguagePreference.English,
            Theme = AppThemePreference.Dark,
            MinimizeToTrayOnClose = false,
            LaunchAtStartup = false,
            StartMinimized = true,
            CheckForUpdates = false,
            NotifyWhenUpdateAvailable = false,
            ShareAnonymousTelemetry = false,
            ShareCrashReports = false,
            PrivacyConsentVersion = PrivacyConsentPolicy.CurrentVersion,
            DismissedLiveAlertId = "alert-42",
            LastSeenReleaseNotesVersion = "1.2.3"
        };
        var service = new FakeAppOptimizationService(initialSettings, settingsFileExists: true);
        var startupRegistration = new SessionStartupRegistrationService();
        var viewModel = new MainViewModel(service, startupRegistration: startupRegistration);
        await viewModel.InitializeAsync();

        await viewModel.RestoreGeneralSettingsDefaultsAsync();

        var saved = Assert.IsType<AppSettings>(service.SavedSettings);
        Assert.Equal(new AppSettings().Language, saved.Language);
        Assert.Equal(new AppSettings().Theme, saved.Theme);
        Assert.True(saved.MinimizeToTrayOnClose);
        Assert.True(saved.LaunchAtStartup);
        Assert.False(saved.StartMinimized);
        Assert.True(saved.CheckForUpdates);
        Assert.True(saved.NotifyWhenUpdateAvailable);
        Assert.False(saved.ShareAnonymousTelemetry);
        Assert.False(saved.ShareCrashReports);
        Assert.Equal(PrivacyConsentPolicy.CurrentVersion, saved.PrivacyConsentVersion);
        Assert.Equal("alert-42", saved.DismissedLiveAlertId);
        Assert.Equal("1.2.3", saved.LastSeenReleaseNotesVersion);
        Assert.True(startupRegistration.IsEnabled());
        Assert.Equal(1, service.SaveCallCount);
    }

    [Fact]
    public async Task RetrySaveSettingsAsync_FailureIsVisibleAndSuccessfulRetryClearsIt()
    {
        var localization = new LocalizationService(System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        var service = new FakeAppOptimizationService(
            new AppSettings { PrivacyConsentVersion = PrivacyConsentPolicy.CurrentVersion },
            settingsFileExists: true,
            settingsSaveException: new IOException("disk unavailable"));
        var viewModel = new MainViewModel(
            service,
            localization,
            startupRegistration: new SessionStartupRegistrationService());
        await viewModel.InitializeAsync();

        await viewModel.RetrySaveSettingsAsync();

        Assert.Equal(
            $"{localization.GetString("Settings.SaveFailed")} — {localization.Format("Report.ErrorCodeSuffix", Ralven.Contracts.BugCode.APP_SETTINGS_PERSISTENCE)}",
            viewModel.SettingsSaveErrorMessage);

        service.SettingsSaveException = null;
        await viewModel.RetrySaveSettingsAsync();

        Assert.Null(viewModel.SettingsSaveErrorMessage);
        Assert.NotNull(service.SavedSettings);
    }

    private static MainViewModel CreateViewModel(AppSettings settings) => new(
        new FakeAppOptimizationService(settings, settingsFileExists: true),
        startupRegistration: new SessionStartupRegistrationService());
}
