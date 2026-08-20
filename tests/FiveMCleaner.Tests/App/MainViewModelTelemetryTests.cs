using FiveMCleaner.App.Services;
using FiveMCleaner.App.ViewModels;
using FiveMCleaner.Contracts;
using Xunit;

namespace FiveMCleaner.Tests.App;

/// <summary>
/// Guards the anonymous telemetry contract at the
/// <see cref="MainViewModel"/> boundary: every event produced by an
/// optimization run must be small enough for
/// <see cref="TelemetryEventValidator"/> to accept, even when the underlying
/// plan legitimately contains far more actions than the wire format allows.
/// </summary>
public sealed class MainViewModelTelemetryTests
{
    [Fact]
    public async Task StartOptimizationAsync_LargePlan_TruncatesTelemetryActionIdsToTheValidatorLimit()
    {
        var service = new SucceedingAppOptimizationService();
        var telemetry = new CapturingTelemetryService();
        var viewModel = new MainViewModel(service, telemetry: telemetry);
        await viewModel.InitializeAsync();

        // The real catalog produces an executable plan with more actions than
        // the telemetry wire format allows; that is what used to make the
        // validator reject the event and silently drop it.
        Assert.True(viewModel.PlannedActions.Count > TelemetryEventValidator.MaxActionIds);

        await viewModel.StartOptimizationAsync();

        var sent = await telemetry.WaitForEventAsync();
        Assert.Equal(TelemetryEventValidator.MaxActionIds, sent.ActionIds?.Count);
        TelemetryEventValidator.Validate(sent);
    }

    /// <summary>Runs a plan to successful completion instead of throwing, so
    /// the whole <see cref="MainViewModel.StartOptimizationAsync"/> telemetry
    /// path (including the <c>finally</c> block) is exercised.</summary>
    private sealed class SucceedingAppOptimizationService : IAppOptimizationService
    {
        public string LogsDirectory => throw new NotSupportedException();

        public bool SettingsFileExists() => true;

        public Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppSettings());

        public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<AppDiagnostic> DiagnoseAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppDiagnostic
            {
                Edition = FiveMEdition.Legacy,
                IsFiveMRunning = false,
                GtaVDetected = false,
                GtaVIsRunning = false,
                GtaVGraphicsSettingsPath = string.Empty,
                CpuName = "Test CPU",
                GpuName = "Test GPU",
                TotalMemoryGiB = 16,
                AvailableMemoryGiB = 8,
                LogicalProcessorCount = 8,
                FreeDiskGiB = 100,
                LegacyCacheBytes = 0,
                OsLabel = "Windows 11",
                ReadinessScore = 80,
                RecommendedProfile = OptimizationProfile.Balanced,
                PerformancePressure = PerformancePressureLevel.Low,
                StreamingSoftware = new StreamingSoftwareSnapshot([], DateTimeOffset.UtcNow, true, true)
            });

        public Task<IReadOnlyList<AppHistoryRecord>> LoadHistoryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AppHistoryRecord>>([]);

        public Task<AppOptimizationResult> ExecuteAsync(
            OptimizationPlanDto plan,
            IProgress<AppProgressUpdate> progress,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppOptimizationResult
            {
                TransactionId = Guid.NewGuid(),
                Succeeded = true,
                WasCancelled = false,
                Summary = "done",
                CompletedActions = plan.Actions.Count,
                BytesFreed = 0
            });

        public Task<bool> RollbackAsync(
            Guid transactionId,
            IProgress<AppProgressUpdate> progress,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AppGtaVBenchmarkResult> RunGtaVBenchmarkAsync(
            int iterations,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingTelemetryService : IAnonymousTelemetryService
    {
        private readonly TaskCompletionSource<AnonymousTelemetryEvent> captured =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsEnabled { get; private set; }

        public void SetEnabled(bool enabled) => IsEnabled = enabled;

        public Task<AnonymousTelemetryEvent> WaitForEventAsync() => captured.Task;

        public Task TrackAsync(AnonymousTelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
        {
            captured.TrySetResult(telemetryEvent);
            return Task.CompletedTask;
        }

        public long SuccessfulSends => 0;
        public long FailedSends => 0;
        public bool IsHealthy => true;
    }
}
