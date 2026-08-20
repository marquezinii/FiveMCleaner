using FiveMCleaner.App.Services;
using FiveMCleaner.App.ViewModels;
using Xunit;

namespace FiveMCleaner.Tests.App;

/// <summary>
/// Exercises the wiring between <see cref="MainViewModel"/> and
/// <see cref="ReleaseNotesEvaluator"/>: that settings loaded during
/// <see cref="MainViewModel.InitializeAsync"/> produce a decision, and that
/// <see cref="MainViewModel.ConfirmReleaseNotesSeenAsync"/> persists through
/// the existing settings mechanism. <see cref="ReleaseNotesCatalog.Versions"/>
/// is empty in this repository right now (see its own doc comment), so these
/// tests cannot exercise "the panel actually shows" end to end without
/// depending on the real, uncontrollable assembly version — that scenario is
/// covered without that dependency by <see cref="ReleaseNotesEvaluatorTests"/>,
/// which takes the app version as an explicit parameter instead of reading it
/// from the running assembly.
/// </summary>
public sealed class MainViewModelReleaseNotesTests
{
    [Fact]
    public async Task InitializeAsync_NewInstallation_ComputesADecisionThatNeverShows()
    {
        var service = new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false);
        var viewModel = new MainViewModel(service);

        await viewModel.InitializeAsync();

        var decision = viewModel.PendingReleaseNotes;
        Assert.NotNull(decision);
        Assert.False(decision!.ShouldShow);
        Assert.True(decision.ShouldRecordSilently);
    }

    [Fact]
    public async Task InitializeAsync_ExistingInstallationWithEmptyCatalog_NeverShowsButStillRecordsSilently()
    {
        var settings = new AppSettings { LastSeenReleaseNotesVersion = "0.1.0" };
        var service = new FakeAppOptimizationService(settings, settingsFileExists: true);
        var viewModel = new MainViewModel(service);

        await viewModel.InitializeAsync();

        var decision = viewModel.PendingReleaseNotes;
        Assert.NotNull(decision);
        Assert.False(decision!.ShouldShow);
        // The real catalog is empty, so whatever version is currently
        // running has no entry — nothing to show, but still worth recording
        // as the new baseline.
        Assert.True(decision.ShouldRecordSilently);
    }

    [Fact]
    public async Task ConfirmReleaseNotesSeenAsync_PersistsTheGivenVersionThroughTheExistingSettingsPath()
    {
        var service = new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false);
        var viewModel = new MainViewModel(service);
        await viewModel.InitializeAsync();

        await viewModel.ConfirmReleaseNotesSeenAsync("1.9.0");

        Assert.NotNull(service.SavedSettings);
        Assert.Equal("1.9.0", service.SavedSettings!.LastSeenReleaseNotesVersion);
        Assert.Null(viewModel.PendingReleaseNotes);
    }

    [Fact]
    public async Task ConfirmReleaseNotesSeenAsync_PreservesEveryOtherExistingSetting()
    {
        var oldSettings = new AppSettings
        {
            Language = AppLanguagePreference.English,
            Theme = AppThemePreference.Dark,
            MinimizeToTrayOnClose = false,
            LaunchAtStartup = true,
            CheckForUpdates = false,
            LastSeenReleaseNotesVersion = "1.0.0"
        };
        var service = new FakeAppOptimizationService(oldSettings, settingsFileExists: true);
        var viewModel = new MainViewModel(service);
        await viewModel.InitializeAsync();

        await viewModel.ConfirmReleaseNotesSeenAsync("1.9.0");

        Assert.Equal(AppLanguagePreference.English, service.SavedSettings!.Language);
        Assert.Equal(AppThemePreference.Dark, service.SavedSettings.Theme);
        Assert.False(service.SavedSettings.CheckForUpdates);
        Assert.Equal("1.9.0", service.SavedSettings.LastSeenReleaseNotesVersion);
    }

    [Fact]
    public async Task ConfirmReleaseNotesSeenAsync_SurvivesAcrossANewViewModelInstance_LikeARealRestart()
    {
        // Simulates persistence surviving a restart: the same in-memory
        // "disk" (FakeAppOptimizationService.SavedSettings promoted to the
        // next load) is handed to a brand-new MainViewModel, mirroring how a
        // real settings.json file would be read back on the next launch.
        var firstRunService = new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false);
        var firstRunViewModel = new MainViewModel(firstRunService);
        await firstRunViewModel.InitializeAsync();
        await firstRunViewModel.ConfirmReleaseNotesSeenAsync("1.9.0");

        var secondRunService = new FakeAppOptimizationService(
            firstRunService.SavedSettings!,
            settingsFileExists: true);
        var secondRunViewModel = new MainViewModel(secondRunService);
        await secondRunViewModel.InitializeAsync();

        Assert.False(secondRunViewModel.PendingReleaseNotes!.ShouldShow);
    }
}
