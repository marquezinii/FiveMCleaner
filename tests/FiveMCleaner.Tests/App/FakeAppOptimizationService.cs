using FiveMCleaner.App.Services;
using FiveMCleaner.Contracts;

namespace FiveMCleaner.Tests.App;

/// <summary>
/// Minimal, in-memory double of <see cref="IAppOptimizationService"/> used
/// only to exercise <see cref="FiveMCleaner.App.ViewModels.MainViewModel"/>'s
/// privacy consent wiring (loading settings, computing the consent decision,
/// and persisting the outcome) without touching real disk. Methods the
/// consent flow does not call throw <see cref="NotSupportedException"/> so a
/// test that accidentally depends on unrelated behavior fails loudly instead
/// of silently returning a meaningless default.
/// </summary>
public sealed class FakeAppOptimizationService : IAppOptimizationService
{
    private readonly bool settingsFileExists;
    public AppSettings? SavedSettings { get; private set; }
    public int SaveCallCount { get; private set; }

    public FakeAppOptimizationService(AppSettings initialSettings, bool settingsFileExists, Exception? diagnosticException = null)
    {
        InitialSettings = initialSettings;
        this.settingsFileExists = settingsFileExists;
        DiagnosticException = diagnosticException;
    }

    public AppSettings InitialSettings { get; }

    public Exception? DiagnosticException { get; }

    public string LogsDirectory => throw new NotSupportedException();

    public bool SettingsFileExists() => settingsFileExists;

    public Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(InitialSettings);

    public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        SavedSettings = settings;
        SaveCallCount++;
        return Task.CompletedTask;
    }

    public Task<AppDiagnostic> DiagnoseAsync(CancellationToken cancellationToken = default) =>
        DiagnosticException is null
            ? Task.FromResult(CreateMinimalDiagnostic())
            : Task.FromException<AppDiagnostic>(DiagnosticException);

    public Task<IReadOnlyList<AppHistoryRecord>> LoadHistoryAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AppHistoryRecord>>([]);

    public Task<AppOptimizationResult> ExecuteAsync(
        OptimizationPlanDto plan,
        IProgress<AppProgressUpdate> progress,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool> RollbackAsync(
        Guid transactionId,
        IProgress<AppProgressUpdate> progress,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<AppGtaVBenchmarkResult> RunGtaVBenchmarkAsync(
        int iterations,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    private static AppDiagnostic CreateMinimalDiagnostic() => new()
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
    };
}

/// <summary>
/// Records whether telemetry was enabled/disabled and whether any event was
/// ever tracked, so tests can assert that confirming (or declining) privacy
/// consent never sends anything by itself.
/// </summary>
public sealed class RecordingTelemetryService : IAnonymousTelemetryService
{
    public bool IsEnabled { get; private set; }

    public int TrackCallCount { get; private set; }

    public void SetEnabled(bool enabled) => IsEnabled = enabled;

    public Task TrackAsync(AnonymousTelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
    {
        TrackCallCount++;
        return Task.CompletedTask;
    }

    public long SuccessfulSends => 0;
    public long FailedSends => 0;
    public bool IsHealthy => true;
}
