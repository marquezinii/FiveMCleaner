using FiveMCleaner.App.Services;
using FiveMCleaner.App.ViewModels;
using Xunit;

namespace FiveMCleaner.Tests.App;

/// <summary>
/// Exercises <see cref="MainViewModel.CheckLiveAlertAsync"/> and
/// <see cref="MainViewModel.DismissLiveAlert"/> -- the admin-broadcast banner
/// and its persistent warning icon. See
/// docs/superpowers/specs/2026-08-17-live-alerts-design.md for the full
/// behavior this is meant to guard.
/// </summary>
public sealed class MainViewModelLiveAlertTests
{
    private static MainViewModel CreateViewModel(
        ILiveAlertService? liveAlertService,
        FakeAppOptimizationService? optimizationService = null) => new(
        optimizationService ?? new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false),
        liveAlertService: liveAlertService);

    [Fact]
    public async Task CheckLiveAlertAsync_ActiveAlert_ShowsBannerAndIcon()
    {
        var service = new FakeLiveAlertService(new LiveAlertSnapshot("v1", "Entre no Discord", true));
        var viewModel = CreateViewModel(service);

        await viewModel.CheckLiveAlertAsync();

        Assert.True(viewModel.IsLiveAlertBannerVisible);
        Assert.True(viewModel.IsLiveAlertIconVisible);
        Assert.Equal("Entre no Discord", viewModel.LiveAlertMessage);
    }

    [Fact]
    public async Task CheckLiveAlertAsync_InactiveAlert_HidesBannerAndIcon()
    {
        var service = new FakeLiveAlertService(new LiveAlertSnapshot(null, string.Empty, false));
        var viewModel = CreateViewModel(service);

        await viewModel.CheckLiveAlertAsync();

        Assert.False(viewModel.IsLiveAlertBannerVisible);
        Assert.False(viewModel.IsLiveAlertIconVisible);
    }

    [Fact]
    public async Task CheckLiveAlertAsync_AlreadyDismissedId_KeepsIconVisibleButHidesBanner()
    {
        var settings = new FakeAppOptimizationService(
            new AppSettings { DismissedLiveAlertId = "v1" },
            settingsFileExists: true);
        var service = new FakeLiveAlertService(new LiveAlertSnapshot("v1", "Entre no Discord", true));
        var viewModel = CreateViewModel(service, settings);
        await viewModel.InitializeAsync();

        await viewModel.CheckLiveAlertAsync();

        Assert.False(viewModel.IsLiveAlertBannerVisible);
        Assert.True(viewModel.IsLiveAlertIconVisible);
    }

    [Fact]
    public async Task CheckLiveAlertAsync_NewerIdThanDismissed_ShowsBannerAgain()
    {
        var settings = new FakeAppOptimizationService(
            new AppSettings { DismissedLiveAlertId = "v1" },
            settingsFileExists: true);
        var service = new FakeLiveAlertService(new LiveAlertSnapshot("v2", "Novo aviso", true));
        var viewModel = CreateViewModel(service, settings);
        await viewModel.InitializeAsync();

        await viewModel.CheckLiveAlertAsync();

        Assert.True(viewModel.IsLiveAlertBannerVisible);
    }

    [Fact]
    public async Task CheckLiveAlertAsync_TransportFailure_LeavesCurrentStateUntouchedInsteadOfThrowing()
    {
        var service = new FakeLiveAlertService(exceptionToThrow: new HttpRequestException("boom"));
        var viewModel = CreateViewModel(service);

        await viewModel.CheckLiveAlertAsync();

        Assert.False(viewModel.IsLiveAlertBannerVisible);
        Assert.False(viewModel.IsLiveAlertIconVisible);
    }

    [Fact]
    public async Task CheckLiveAlertAsync_NoServiceConfigured_DoesNothingAndNeverThrows()
    {
        var viewModel = CreateViewModel(liveAlertService: null);

        await viewModel.CheckLiveAlertAsync();

        Assert.False(viewModel.IsLiveAlertBannerVisible);
        Assert.False(viewModel.IsLiveAlertIconVisible);
    }

    [Fact]
    public async Task DismissLiveAlert_HidesBannerButKeepsIconAndPersistsTheDismissedId()
    {
        var settings = new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false);
        var service = new FakeLiveAlertService(new LiveAlertSnapshot("v1", "Entre no Discord", true));
        var viewModel = CreateViewModel(service, settings);
        await viewModel.InitializeAsync();
        await viewModel.CheckLiveAlertAsync();

        viewModel.DismissLiveAlert();

        Assert.False(viewModel.IsLiveAlertBannerVisible);
        Assert.True(viewModel.IsLiveAlertIconVisible);
        Assert.NotNull(settings.SavedSettings);
        Assert.Equal("v1", settings.SavedSettings!.DismissedLiveAlertId);
    }

    [Fact]
    public void DismissLiveAlert_NoBannerVisible_DoesNothing()
    {
        var settings = new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false);
        var viewModel = CreateViewModel(liveAlertService: null, settings);

        viewModel.DismissLiveAlert();

        Assert.Equal(0, settings.SaveCallCount);
    }
}
