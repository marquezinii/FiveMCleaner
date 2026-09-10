using System.Globalization;
using Ralven.App.Services;
using Ralven.App.ViewModels;
using Ralven.Contracts;
using Xunit;

namespace Ralven.Tests.App;

public sealed class MainViewModelStartupTests
{
    private static AppSettings SavedPreferences => new()
    {
        Theme = AppThemePreference.Dark,
        Language = AppLanguagePreference.Spanish,
        ShareAnonymousTelemetry = false,
        ShareCrashReports = false,
        PrivacyConsentVersion = PrivacyConsentPolicy.CurrentVersion,
        MinimizeToTrayOnClose = true,
        StartMinimized = true
    };

    [Fact]
    public async Task FailedDiagnostic_PreservesPreferencesAndDeclinedOptionalReports()
    {
        var service = new FakeAppOptimizationService(
            SavedPreferences, settingsFileExists: true,
            diagnosticException: new InvalidOperationException("Unavailable probe"));
        var telemetry = new RecordingTelemetryService();
        using var viewModel = Create(service, telemetry: telemetry);

        await viewModel.InitializeAsync(startBackgroundServices: false);

        AssertPreferences(viewModel);
        Assert.False(viewModel.CanStart);
        Assert.False(telemetry.IncludesOptionalData);
        Assert.Equal(0, service.SaveCallCount);
    }

    [Fact]
    public async Task PendingDiagnostic_AppliesPreferencesBeforeReadiness()
    {
        var inner = new FakeAppOptimizationService(SavedPreferences, settingsFileExists: true);
        var diagnosis = new TaskCompletionSource<AppDiagnostic>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var viewModel = Create(new PendingDiagnosticService(inner, diagnosis.Task));

        var initialization = viewModel.InitializeAsync(startBackgroundServices: false);

        Assert.False(initialization.IsCompleted);
        AssertPreferences(viewModel);
        Assert.False(viewModel.CanStart);
        Assert.False(viewModel.CanRefresh);
        diagnosis.SetResult(await inner.DiagnoseAsync(TestContext.Current.CancellationToken));
        await initialization.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(viewModel.CanRefresh);
    }

    [Fact]
    public async Task DeferredBackgroundServices_StartOnceAfterLocalInitialization()
    {
        var updates = new FakeReleaseUpdateService();
        var alerts = new FakeLiveAlertService();
        using var viewModel = Create(
            new FakeAppOptimizationService(SavedPreferences, settingsFileExists: true), updates, alerts);

        await viewModel.InitializeAsync(startBackgroundServices: false);

        Assert.Equal(0, updates.CheckForUpdateCallCount);
        Assert.Equal(0, alerts.CallCount);
        viewModel.StartBackgroundServices();
        viewModel.StartBackgroundServices();
        Assert.Equal(1, updates.CheckForUpdateCallCount);
        Assert.Equal(1, alerts.CallCount);
    }

    [Fact]
    public async Task DisposedBeforeDeferredStartup_DoesNotContactRemoteServices()
    {
        var updates = new FakeReleaseUpdateService();
        var alerts = new FakeLiveAlertService();
        var viewModel = Create(
            new FakeAppOptimizationService(SavedPreferences, settingsFileExists: true), updates, alerts);
        await viewModel.InitializeAsync(startBackgroundServices: false);

        viewModel.Dispose();
        viewModel.StartBackgroundServices();

        Assert.Equal(0, updates.CheckForUpdateCallCount);
        Assert.Equal(0, alerts.CallCount);
    }

    private static MainViewModel Create(
        IAppOptimizationService service,
        IReleaseUpdateService? updates = null,
        ILiveAlertService? alerts = null,
        IAnonymousTelemetryService? telemetry = null) => new(
            service,
            localization: new LocalizationService(CultureInfo.GetCultureInfo("en-US")),
            startupRegistration: new SessionStartupRegistrationService(),
            releaseUpdateService: updates,
            liveAlertService: alerts,
            telemetry: telemetry);

    private static void AssertPreferences(MainViewModel viewModel)
    {
        Assert.Equal(AppThemePreference.Dark, viewModel.ThemePreference);
        Assert.Equal(AppLanguagePreference.Spanish, viewModel.LanguagePreference);
        Assert.False(viewModel.ShareOptionalReports);
        Assert.True(viewModel.MinimizeToTrayOnClose);
        Assert.True(viewModel.StartMinimized);
        Assert.NotNull(viewModel.PrivacyConsentDecision);
        Assert.False(viewModel.PrivacyConsentDecision.RequiresConsentScreen);
        Assert.False(viewModel.PrivacyConsentDecision.AreOptionalReportsAuthorized);
    }

    private sealed class PendingDiagnosticService(
        FakeAppOptimizationService inner, Task<AppDiagnostic> diagnosis) : IAppOptimizationService
    {
        public string LogsDirectory => inner.LogsDirectory;
        public bool SettingsFileExists() => inner.SettingsFileExists();
        public Task<AppDiagnostic> DiagnoseAsync(CancellationToken cancellationToken = default) => diagnosis;
        public Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default) => inner.LoadSettingsAsync(cancellationToken);
        public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default) => inner.SaveSettingsAsync(settings, cancellationToken);
        public Task<IReadOnlyList<AppHistoryRecord>> LoadHistoryAsync(CancellationToken cancellationToken = default) => inner.LoadHistoryAsync(cancellationToken);
        public Task<OptimizationReportDto?> LoadReportAsync(Guid transactionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AppOptimizationResult> ExecuteAsync(OptimizationPlanDto plan, IProgress<AppProgressUpdate> progress, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RollbackAsync(Guid transactionId, IProgress<AppProgressUpdate> progress, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AppGtaVBenchmarkResult> RunGtaVBenchmarkAsync(int iterations, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
