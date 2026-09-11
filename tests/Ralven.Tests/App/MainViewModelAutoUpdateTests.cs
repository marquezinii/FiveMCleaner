using Ralven.App.Services;
using Ralven.App.ViewModels;
using Ralven.Contracts;
using Xunit;

namespace Ralven.Tests.App;

/// <summary>
/// Exercises the one-click silent auto-update flow: download the verified
/// installer, hand it to <see cref="ISilentUpdateInstaller"/>, and reflect the
/// outcome in the update banner. Also guards the rule that updating -- which
/// replaces the running executable -- must never be offered while an
/// optimization or rollback is in flight.
/// </summary>
public sealed class MainViewModelAutoUpdateTests
{
    private static DownloadedUpdate CreateDownload(string version = "9.9.9") => new(
        StableSemanticVersion.Parse(version),
        InstallerPath: $@"C:\Updates\Ralven-Setup-{version}-win-x64.exe",
        SizeBytes: 10 * 1024 * 1024,
        Sha256Hex: new string('a', 64),
        WasAlreadyDownloaded: false);

    private static HistoryDisplayItem CreateRollbackableHistoryItem() => new(
        TransactionId: Guid.NewGuid(),
        Title: "Balanced",
        DateLabel: "today",
        Summary: "summary",
        CanRollback: true);

    [Fact]
    public async Task DownloadAndInstallUpdateAsync_Success_StartsTheSilentInstallerAndReturnsTrue()
    {
        var update = FakeReleaseUpdateService.CreateUpdate("9.9.9");
        var download = CreateDownload("9.9.9");
        var releaseUpdateService = new FakeReleaseUpdateService(
            updateToReturn: update,
            downloadToReturn: download);
        var installer = new FakeSilentUpdateInstaller(SilentUpdateLaunch.Running());
        var viewModel = new MainViewModel(
            new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false),
            releaseUpdateService: releaseUpdateService,
            silentUpdateInstaller: installer);
        await viewModel.CheckForUpdatesManuallyAsync();

        var started = await viewModel.DownloadAndInstallUpdateAsync();

        Assert.True(started);
        Assert.Equal(1, installer.StartCallCount);
        Assert.Equal(download.InstallerPath, installer.LastRequestedUpdate?.InstallerPath);
    }

    [Fact]
    public async Task DownloadAndInstallUpdateAsync_InstallerReportsFailure_KeepsBannerVisibleAndReturnsFalse()
    {
        var update = FakeReleaseUpdateService.CreateUpdate("9.9.9");
        var download = CreateDownload("9.9.9");
        var releaseUpdateService = new FakeReleaseUpdateService(
            updateToReturn: update,
            downloadToReturn: download);
        var installer = new FakeSilentUpdateInstaller(
            SilentUpdateLaunch.Failed(3, "instalador corrompido"));
        var viewModel = new MainViewModel(
            new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false),
            releaseUpdateService: releaseUpdateService,
            silentUpdateInstaller: installer);
        await viewModel.CheckForUpdatesManuallyAsync();

        var started = await viewModel.DownloadAndInstallUpdateAsync();

        Assert.False(started);
        // The app must stay open and able to retry -- never stuck mid-update.
        Assert.True(viewModel.IsUpdateBannerVisible);
        Assert.False(viewModel.IsInstallingUpdate);
    }

    [Fact]
    public async Task DownloadAndInstallUpdateAsync_NoInstallerConfigured_DoesNothingAndNeverThrows()
    {
        var update = FakeReleaseUpdateService.CreateUpdate("9.9.9");
        var download = CreateDownload("9.9.9");
        var releaseUpdateService = new FakeReleaseUpdateService(
            updateToReturn: update,
            downloadToReturn: download);
        var viewModel = new MainViewModel(
            new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false),
            releaseUpdateService: releaseUpdateService);
        await viewModel.CheckForUpdatesManuallyAsync();

        var started = await viewModel.DownloadAndInstallUpdateAsync();

        Assert.False(started);
    }

    [Fact]
    public async Task InstallDownloadedUpdateAsync_LauncherThrows_IsCaughtAndReportedAsFailure()
    {
        var installer = new FakeSilentUpdateInstaller(exceptionToThrow: new InvalidOperationException("boom"));
        var viewModel = new MainViewModel(
            new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false),
            silentUpdateInstaller: installer);

        var started = await viewModel.InstallDownloadedUpdateAsync(CreateDownload());

        Assert.False(started);
        Assert.False(viewModel.IsInstallingUpdate);
    }

    [Fact]
    public async Task CanDownloadUpdate_IsFalseWhileARollbackIsInProgress()
    {
        // Updating replaces the running executable, so it must never be
        // offered while the app is mid-transaction -- there would be no safe
        // way to finish or revert the operation that was interrupted.
        var update = FakeReleaseUpdateService.CreateUpdate("9.9.9");
        var releaseUpdateService = new FakeReleaseUpdateService(updateToReturn: update);
        var rollbackGate = new TaskCompletionSource<bool>();
        var service = new BlockingRollbackOptimizationService(rollbackGate.Task);
        var viewModel = new MainViewModel(
            service,
            releaseUpdateService: releaseUpdateService);
        await viewModel.CheckForUpdatesManuallyAsync();
        Assert.True(viewModel.CanDownloadUpdate);

        var rollbackTask = viewModel.RollbackAsync(CreateRollbackableHistoryItem());

        Assert.False(viewModel.CanDownloadUpdate);
        rollbackGate.SetResult(true);
        await rollbackTask;
        Assert.True(viewModel.CanDownloadUpdate);
    }

    /// <summary>Blocks <see cref="MainViewModel.RollbackAsync"/> on a
    /// caller-controlled gate so the test can observe
    /// <see cref="MainViewModel.IsBusy"/> mid-flight. Only the members
    /// actually exercised by <c>RollbackAsync</c> are implemented; anything
    /// else throws loudly rather than returning a silent default.</summary>
    private sealed class BlockingRollbackOptimizationService(Task<bool> gate) : IAppOptimizationService
    {
        public string LogsDirectory => throw new NotSupportedException();

        public bool SettingsFileExists() => throw new NotSupportedException();

        public Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppSettings());

        public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AppDiagnostic> DiagnoseAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AppHistoryRecord>> LoadHistoryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AppHistoryRecord>>([]);

        public Task<OptimizationReportDto?> LoadReportAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AppOptimizationResult> ExecuteAsync(
            OptimizationPlanDto plan,
            IProgress<AppProgressUpdate> progress,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> RollbackAsync(
            Guid transactionId,
            IProgress<AppProgressUpdate> progress,
            CancellationToken cancellationToken = default) => gate;

        public Task<AppGtaVBenchmarkResult> RunGtaVBenchmarkAsync(
            int iterations,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
