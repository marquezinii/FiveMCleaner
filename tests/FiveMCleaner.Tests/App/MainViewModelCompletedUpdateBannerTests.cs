using FiveMCleaner.App.Services;
using FiveMCleaner.App.ViewModels;
using Xunit;

namespace FiveMCleaner.Tests.App;

/// <summary>
/// Covers the post-update confirmation banner shown after the installer
/// relaunches the app with <c>--updated=X.Y.Z</c>, including dismissal.
/// </summary>
public sealed class MainViewModelCompletedUpdateBannerTests
{
    [Fact]
    public void ReportCompletedUpdate_ShowsBannerWithoutActionButtons()
    {
        var viewModel = new MainViewModel(
            new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false));

        viewModel.ReportCompletedUpdate("1.2.3");

        Assert.True(viewModel.IsUpdateBannerVisible);
        Assert.True(viewModel.IsUpdateCompletedBannerVisible);
        Assert.False(viewModel.IsUpdateActionVisible);
        Assert.Equal("1.2.3", viewModel.JustUpdatedToVersion);
    }

    [Fact]
    public void DismissCompletedUpdateBanner_HidesBannerAndClearsState()
    {
        var viewModel = new MainViewModel(
            new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false));
        viewModel.ReportCompletedUpdate("1.2.3");

        viewModel.DismissCompletedUpdateBanner();

        Assert.False(viewModel.IsUpdateBannerVisible);
        Assert.False(viewModel.IsUpdateCompletedBannerVisible);
        Assert.Null(viewModel.JustUpdatedToVersion);
        Assert.True(viewModel.IsUpdateActionVisible);
    }

    [Fact]
    public void DismissCompletedUpdateBanner_WhenNoCompletedUpdate_IsNoOp()
    {
        var viewModel = new MainViewModel(
            new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false));

        viewModel.DismissCompletedUpdateBanner();

        Assert.False(viewModel.IsUpdateBannerVisible);
        Assert.Null(viewModel.JustUpdatedToVersion);
    }
}
