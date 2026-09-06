using Ralven.App.Services;
using Ralven.App.ViewModels;
using Ralven.Contracts;
using Ralven.Core.Catalog;
using Ralven.Windows.Infrastructure;
using Xunit;

namespace Ralven.Tests.App;

public sealed class MainViewModelEmptyPlanTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("pt-BR")]
    [InlineData("es")]
    public void GeneralOptimizerCopy_HasAnIndependentProductIdentity(string cultureName)
    {
        var localization = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo(cultureName));
        var subtitle = localization.GetString("Optimizer.General.Subtitle");

        Assert.DoesNotContain("FiveM", subtitle, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GTA", subtitle, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BeforeDiagnostic_NoProfileIsPresentedAsRecommended()
    {
        var viewModel = new MainViewModel(new FakeAppOptimizationService(
            new AppSettings(),
            settingsFileExists: false));

        Assert.False(viewModel.IsLightRecommended);
        Assert.False(viewModel.IsBalancedRecommended);
        Assert.False(viewModel.IsAggressiveRecommended);
        Assert.False(viewModel.IsSelectedProfileRecommended);
    }

    [Theory]
    [InlineData(OptimizationProfile.Light)]
    [InlineData(OptimizationProfile.Balanced)]
    [InlineData(OptimizationProfile.Aggressive)]
    public async Task InitializeAsync_SelectsTheProfileRecommendedByHardware(
        OptimizationProfile recommendedProfile)
    {
        var viewModel = new MainViewModel(new FakeAppOptimizationService(
            new AppSettings(),
            settingsFileExists: false,
            recommendedProfile: recommendedProfile));

        await viewModel.InitializeAsync();

        Assert.Equal(recommendedProfile == OptimizationProfile.Light, viewModel.IsLightSelected);
        Assert.Equal(recommendedProfile == OptimizationProfile.Balanced, viewModel.IsBalancedSelected);
        Assert.Equal(recommendedProfile == OptimizationProfile.Aggressive, viewModel.IsAggressiveSelected);
        Assert.True(viewModel.IsSelectedProfileRecommended);
    }

    [Fact]
    public async Task InitializeAsync_WhenDiagnosticFails_DoesNotClaimFiveMIsMissing()
    {
        var service = new FakeAppOptimizationService(
            new AppSettings(),
            settingsFileExists: false,
            diagnosticException: new IOException("diagnostic failed"));
        var localization = new LocalizationService(System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        var viewModel = new MainViewModel(service, localization);

        await viewModel.InitializeAsync();

        Assert.Equal(
            localization.GetString("Plan.Empty.DiagnosticUnavailable"),
            viewModel.EmptyPlanMessage);
        Assert.False(viewModel.CanStart);
    }

    [Fact]
    public async Task GeneralWindows_IsTheDefaultScopeAndDoesNotRequireFiveMInstallation()
    {
        var service = new FakeAppOptimizationService(
            new AppSettings(),
            settingsFileExists: false,
            edition: FiveMEdition.Unknown);
        var localization = new LocalizationService(System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        var viewModel = new MainViewModel(service, localization);

        await viewModel.InitializeAsync();

        Assert.Equal(OptimizationScope.GeneralWindows, viewModel.OptimizationScope);
        Assert.Equal(localization.GetString("Optimizer.General.Title"), viewModel.OptimizerTitle);
        Assert.True(viewModel.CanStart);

        viewModel.SelectProfile(OptimizationProfile.Aggressive);

        Assert.Contains(
            localization.GetString("Plan.Notice.AggressiveWindows"),
            viewModel.PlanNoticesText,
            StringComparison.Ordinal);

        viewModel.SetOptimizationScope(OptimizationScope.FiveMLegacy);

        Assert.Equal(localization.GetString("Optimizer.FiveM.Title"), viewModel.OptimizerTitle);
        Assert.False(viewModel.CanStart);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RunningGameProcess_BlocksOnlyTheFiveMLegacyScope(
        bool isFiveMRunning,
        bool gtaVIsRunning)
    {
        var viewModel = new MainViewModel(new FakeAppOptimizationService(
            new AppSettings(),
            settingsFileExists: false,
            isFiveMRunning: isFiveMRunning,
            gtaVIsRunning: gtaVIsRunning));

        await viewModel.InitializeAsync();

        Assert.Equal(OptimizationScope.GeneralWindows, viewModel.OptimizationScope);
        Assert.True(viewModel.CanStart);

        viewModel.SetOptimizationScope(OptimizationScope.FiveMLegacy);

        Assert.False(viewModel.CanStart);
    }

    [Fact]
    public async Task ActiveMonitoredSession_BlocksOnlyTheFiveMLegacyScope()
    {
        using var viewModel = new MainViewModel(
            new FakeAppOptimizationService(
                new AppSettings(),
                settingsFileExists: false,
                fiveMRoot: Path.Combine("C:", "FiveM")),
            fiveMSessionProbe: _ => FiveMSessionPresence.Present);

        await viewModel.InitializeAsync();
        viewModel.ToggleFiveMSessionMonitor();
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!viewModel.IsFiveMSessionActive && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(viewModel.IsFiveMSessionActive);
        Assert.Equal(OptimizationScope.GeneralWindows, viewModel.OptimizationScope);
        Assert.True(viewModel.CanStart);

        viewModel.SetOptimizationScope(OptimizationScope.FiveMLegacy);

        Assert.False(viewModel.CanStart);
    }

    [Theory]
    [InlineData(OptimizationProfile.Light, OptimizationScope.GeneralWindows)]
    [InlineData(OptimizationProfile.Balanced, OptimizationScope.GeneralWindows)]
    [InlineData(OptimizationProfile.Aggressive, OptimizationScope.GeneralWindows)]
    [InlineData(OptimizationProfile.Light, OptimizationScope.FiveMLegacy)]
    [InlineData(OptimizationProfile.Balanced, OptimizationScope.FiveMLegacy)]
    [InlineData(OptimizationProfile.Aggressive, OptimizationScope.FiveMLegacy)]
    public async Task StandardProfileMatrix_NeverRepairsServerCacheImplicitlyAndScopesSafetyCheck(
        OptimizationProfile profile,
        OptimizationScope scope)
    {
        var viewModel = new MainViewModel(new FakeAppOptimizationService(
            new AppSettings(),
            settingsFileExists: false));

        await viewModel.InitializeAsync();
        viewModel.SelectProfile(profile);
        viewModel.SetOptimizationScope(scope);

        var actionIds = viewModel.PlannedActions.Select(action => action.Id).ToArray();
        Assert.True(viewModel.CanStart);
        Assert.DoesNotContain(OptimizationActionIds.RepairLegacyServerCache, actionIds);
        Assert.Equal(
            scope == OptimizationScope.FiveMLegacy,
            actionIds.Contains(OptimizationActionIds.VerifyFiveMIsStopped, StringComparer.Ordinal));
    }

    [Fact]
    public async Task EnhancedEdition_ExplainsTheSafeBlockInsteadOfClaimingLegacyIsMissing()
    {
        var service = new FakeAppOptimizationService(
            new AppSettings(),
            settingsFileExists: false,
            edition: FiveMEdition.Enhanced);
        var localization = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        var viewModel = new MainViewModel(service, localization);

        await viewModel.InitializeAsync();
        viewModel.SetOptimizationScope(OptimizationScope.FiveMLegacy);

        Assert.Equal(
            localization.GetString("Diagnosis.FiveMEnhancedBlocked"),
            viewModel.EditionLabel);
        Assert.Equal(
            localization.GetString("Diagnosis.EnhancedUnsupported"),
            viewModel.RecommendationText);
        Assert.Equal(
            localization.GetString("Diagnosis.EnhancedUnsupported"),
            viewModel.EmptyPlanMessage);
        Assert.False(viewModel.CanStart);
    }
}
