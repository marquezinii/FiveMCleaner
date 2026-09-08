using System.Globalization;
using Ralven.App.Services;
using Ralven.App.ViewModels;
using Ralven.Contracts;
using Ralven.Windows.Infrastructure;
using Xunit;

namespace Ralven.Tests.App;

public sealed class MainViewModelSystemHealthTests
{
    [Fact]
    public async Task RefreshSystemHealth_MapsEachAvailableProviderState()
    {
        var snapshot = new WindowsSystemHealthSnapshot(
            new WindowsSecurityProviderHealth(WindowsSecurityHealthState.Good, 0),
            new WindowsSecurityProviderHealth(WindowsSecurityHealthState.Poor, 0),
            new WindowsSecurityProviderHealth(WindowsSecurityHealthState.Snoozed, 0),
            DateTimeOffset.UtcNow);
        using var viewModel = CreateViewModel(new StubInspector(snapshot));

        await viewModel.InitializeAsync();
        await viewModel.RefreshWindowsSystemHealthAsync();

        Assert.Equal("All good", viewModel.WindowsAntivirusHealthLabel);
        Assert.Equal("Check this", viewModel.WindowsFirewallHealthLabel);
        Assert.Equal("Paused", viewModel.WindowsAutomaticUpdatesHealthLabel);
        Assert.Equal(
            "Windows reported the status of all three protections.",
            viewModel.WindowsSystemHealthStatusMessage);
    }

    [Fact]
    public async Task RefreshSystemHealth_ReportsPartialAndUnavailableReadingsHonestly()
    {
        var snapshot = new WindowsSystemHealthSnapshot(
            new WindowsSecurityProviderHealth(WindowsSecurityHealthState.Good, 0),
            new WindowsSecurityProviderHealth(
                WindowsSecurityHealthState.Unavailable,
                unchecked((int)0x80004005)),
            new WindowsSecurityProviderHealth(WindowsSecurityHealthState.NotMonitored, 0),
            DateTimeOffset.UtcNow);
        using var viewModel = CreateViewModel(new StubInspector(snapshot));

        await viewModel.InitializeAsync();
        await viewModel.RefreshWindowsSystemHealthAsync();

        Assert.Equal("Could not verify", viewModel.WindowsFirewallHealthLabel);
        Assert.Equal("Not monitored", viewModel.WindowsAutomaticUpdatesHealthLabel);
        Assert.StartsWith(
            "Windows could only verify part",
            viewModel.WindowsSystemHealthStatusMessage,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshSystemHealth_ConvertsInspectorFailureToUnavailableState()
    {
        using var viewModel = CreateViewModel(new StubInspector(new InvalidOperationException()));

        await viewModel.InitializeAsync();
        await viewModel.RefreshWindowsSystemHealthAsync();

        Assert.Equal("Could not verify", viewModel.WindowsAntivirusHealthLabel);
        Assert.Equal(
            "Ralven could not verify this information with Windows. — Error code: SEC_HEALTH_QUERY",
            viewModel.WindowsSystemHealthStatusMessage);
        Assert.True(viewModel.CanRefreshWindowsSystemHealth);
    }

    [Fact]
    public async Task InitializeDiagnostic_ExposesInternalPcDetailsAsReady()
    {
        using var viewModel = CreateViewModel(new StubInspector(
            new WindowsSystemHealthSnapshot(
                new WindowsSecurityProviderHealth(WindowsSecurityHealthState.Good, 0),
                new WindowsSecurityProviderHealth(WindowsSecurityHealthState.Good, 0),
                new WindowsSecurityProviderHealth(WindowsSecurityHealthState.Good, 0),
                DateTimeOffset.UtcNow)));

        await viewModel.InitializeAsync();

        Assert.Equal("Test CPU", viewModel.CpuName);
        Assert.Equal("Test GPU", viewModel.GpuDetail);
        Assert.Equal("Windows 11", viewModel.WindowsLabel);
        Assert.Equal(
            "Information updated on this PC.",
            viewModel.SystemPcStatusMessage);
    }

    private static MainViewModel CreateViewModel(IWindowsSystemHealthInspector inspector) => new(
        new FakeAppOptimizationService(
            new AppSettings { Language = AppLanguagePreference.English },
            settingsFileExists: false),
        localization: new LocalizationService(CultureInfo.GetCultureInfo("en-US")),
        windowsSystemHealthInspector: inspector);

    private sealed class StubInspector : IWindowsSystemHealthInspector
    {
        private readonly WindowsSystemHealthSnapshot? snapshot;
        private readonly Exception? exception;

        public StubInspector(WindowsSystemHealthSnapshot snapshot)
        {
            this.snapshot = snapshot;
        }

        public StubInspector(Exception exception)
        {
            this.exception = exception;
        }

        public Task<WindowsSystemHealthSnapshot> InspectAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return exception is null
                ? Task.FromResult(snapshot!)
                : Task.FromException<WindowsSystemHealthSnapshot>(exception);
        }
    }
}
