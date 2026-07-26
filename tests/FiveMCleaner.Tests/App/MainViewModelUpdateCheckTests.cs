using FiveMCleaner.App.Services;
using FiveMCleaner.App.ViewModels;
using Xunit;

namespace FiveMCleaner.Tests.App;

/// <summary>
/// Exercises <see cref="MainViewModel.CheckForUpdatesManuallyAsync"/> -- the
/// "Procurar atualizações" button in Settings. Unlike the silent startup
/// check, this must always report an explicit outcome: either the update
/// banner becomes visible, or <see cref="MainViewModel.ManualUpdateCheckMessage"/>
/// states the app is already on the latest version (or that the check failed).
/// </summary>
public sealed class MainViewModelUpdateCheckTests
{
    [Fact]
    public async Task CheckForUpdatesManuallyAsync_NoUpdateAvailable_SetsUpToDateMessageAndKeepsBannerHidden()
    {
        var releaseUpdateService = new FakeReleaseUpdateService(updateToReturn: null);
        var viewModel = new MainViewModel(
            new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false),
            releaseUpdateService: releaseUpdateService);

        await viewModel.CheckForUpdatesManuallyAsync();

        Assert.Equal(1, releaseUpdateService.CheckForUpdateCallCount);
        Assert.False(viewModel.IsUpdateBannerVisible);
        Assert.False(string.IsNullOrEmpty(viewModel.ManualUpdateCheckMessage));
        Assert.False(viewModel.IsCheckingForUpdatesManually);
    }

    [Fact]
    public async Task CheckForUpdatesManuallyAsync_UpdateAvailable_ShowsBannerAndClearsManualMessage()
    {
        var update = FakeReleaseUpdateService.CreateUpdate("99.0.0");
        var releaseUpdateService = new FakeReleaseUpdateService(updateToReturn: update);
        var viewModel = new MainViewModel(
            new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false),
            releaseUpdateService: releaseUpdateService);

        await viewModel.CheckForUpdatesManuallyAsync();

        Assert.True(viewModel.IsUpdateBannerVisible);
        Assert.Null(viewModel.ManualUpdateCheckMessage);
    }

    [Fact]
    public async Task CheckForUpdatesManuallyAsync_AlreadyKnownUpdate_StillPerformsAFreshCheckUnlikeTheSilentVariant()
    {
        var update = FakeReleaseUpdateService.CreateUpdate("99.0.0");
        var releaseUpdateService = new FakeReleaseUpdateService(updateToReturn: update);
        var viewModel = new MainViewModel(
            new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false),
            releaseUpdateService: releaseUpdateService);
        await viewModel.CheckForUpdatesManuallyAsync();

        await viewModel.CheckForUpdatesManuallyAsync();

        Assert.Equal(2, releaseUpdateService.CheckForUpdateCallCount);
    }

    [Fact]
    public async Task CheckForUpdatesManuallyAsync_TransportFailure_SetsFailureMessageInsteadOfThrowing()
    {
        var releaseUpdateService = new FakeReleaseUpdateService(exceptionToThrow: new HttpRequestException("boom"));
        var viewModel = new MainViewModel(
            new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false),
            releaseUpdateService: releaseUpdateService);

        await viewModel.CheckForUpdatesManuallyAsync();

        Assert.False(viewModel.IsUpdateBannerVisible);
        Assert.Contains("boom", viewModel.ManualUpdateCheckMessage);
        Assert.False(viewModel.IsCheckingForUpdatesManually);
    }

    [Fact]
    public async Task CheckForUpdatesManuallyAsync_NoReleaseUpdateServiceConfigured_DoesNothingAndNeverThrows()
    {
        var viewModel = new MainViewModel(
            new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false));

        await viewModel.CheckForUpdatesManuallyAsync();

        Assert.Null(viewModel.ManualUpdateCheckMessage);
        Assert.False(viewModel.IsCheckingForUpdatesManually);
    }
}
