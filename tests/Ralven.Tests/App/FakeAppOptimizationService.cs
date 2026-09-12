using Ralven.App.Services;
using Ralven.Contracts;

namespace Ralven.Tests.App;

/// <summary>
/// Minimal, in-memory double of <see cref="IAppOptimizationService"/> used
/// only to exercise <see cref="Ralven.App.ViewModels.MainViewModel"/>'s
/// privacy consent wiring (loading settings, computing the consent decision,
/// and persisting the outcome) without touching real disk. Methods the
/// consent flow does not call throw <see cref="NotSupportedException"/> so a
/// test that accidentally depends on unrelated behavior fails loudly instead
/// of silently returning a meaningless default.
/// </summary>
public sealed class FakeAppOptimizationService : IAppOptimizationService
{
    private readonly bool settingsFileExists;
    private readonly IReadOnlyList<AppHistoryRecord> history;
    private readonly OptimizationReportDto? report;
    private readonly bool isFiveMRunning;
    private readonly bool gtaVIsRunning;
    private readonly string? fiveMRoot;
    private readonly FiveMEdition edition;
    private readonly OptimizationProfile recommendedProfile;
    private readonly Task<AppGtaVBenchmarkResult>? benchmarkResult;
    private readonly bool? rollbackResult;
    private readonly AppProgressUpdate? rollbackProgressUpdate;
    public AppSettings? SavedSettings { get; private set; }
    public int SaveCallCount { get; private set; }

    public int HistoryLoadCount { get; private set; }

    public AppOptimizationResult? ExecutionResult { get; set; }

    public Exception? SettingsSaveException { get; set; }

    public FakeAppOptimizationService(
        AppSettings initialSettings,
        bool settingsFileExists,
        Exception? diagnosticException = null,
        IReadOnlyList<AppHistoryRecord>? history = null,
        bool isFiveMRunning = false,
        bool gtaVIsRunning = false,
        string? fiveMRoot = null,
        FiveMEdition edition = FiveMEdition.Legacy,
        OptimizationProfile recommendedProfile = OptimizationProfile.Balanced,
        Task<AppGtaVBenchmarkResult>? benchmarkResult = null,
        bool? rollbackResult = null,
        AppProgressUpdate? rollbackProgressUpdate = null,
        Exception? settingsSaveException = null,
        OptimizationReportDto? report = null)
    {
        InitialSettings = initialSettings;
        this.settingsFileExists = settingsFileExists;
        DiagnosticException = diagnosticException;
        this.history = history ?? [];
        this.isFiveMRunning = isFiveMRunning;
        this.gtaVIsRunning = gtaVIsRunning;
        this.fiveMRoot = fiveMRoot;
        this.edition = edition;
        this.recommendedProfile = recommendedProfile;
        this.benchmarkResult = benchmarkResult;
        this.rollbackResult = rollbackResult;
        this.rollbackProgressUpdate = rollbackProgressUpdate;
        this.report = report;
        SettingsSaveException = settingsSaveException;
    }

    public AppSettings InitialSettings { get; }

    public Exception? DiagnosticException { get; }

    public string LogsDirectory => throw new NotSupportedException();

    public bool SettingsFileExists() => settingsFileExists;

    public Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(InitialSettings);

    public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        SaveCallCount++;
        if (SettingsSaveException is not null)
        {
            return Task.FromException(SettingsSaveException);
        }

        SavedSettings = settings;
        return Task.CompletedTask;
    }

    public Task<AppDiagnostic> DiagnoseAsync(CancellationToken cancellationToken = default) =>
        DiagnosticException is null
            ? Task.FromResult(CreateMinimalDiagnostic(isFiveMRunning, gtaVIsRunning, fiveMRoot, edition, recommendedProfile))
            : Task.FromException<AppDiagnostic>(DiagnosticException);

    public Task<IReadOnlyList<AppHistoryRecord>> LoadHistoryAsync(CancellationToken cancellationToken = default)
    {
        HistoryLoadCount++;
        return Task.FromResult(history);
    }

    public Task<OptimizationReportDto?> LoadReportAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(report?.TransactionId == transactionId ? report : null);

    public Task<AppOptimizationResult> ExecuteAsync(
        OptimizationPlanDto plan,
        IProgress<AppProgressUpdate> progress,
        CancellationToken cancellationToken = default) =>
        ExecutionResult is { } result ? Task.FromResult(result) : throw new NotSupportedException();

    public Task<bool> RollbackAsync(
        Guid transactionId,
        IProgress<AppProgressUpdate> progress,
        CancellationToken cancellationToken = default)
    {
        if (rollbackResult is null)
        {
            throw new NotSupportedException();
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (rollbackProgressUpdate is not null)
        {
            progress.Report(rollbackProgressUpdate);
        }

        return Task.FromResult(rollbackResult.Value);
    }

    public Task<AppGtaVBenchmarkResult> RunGtaVBenchmarkAsync(
        int iterations,
        CancellationToken cancellationToken = default) =>
        benchmarkResult ?? throw new NotSupportedException();

    private static AppDiagnostic CreateMinimalDiagnostic(
        bool isFiveMRunning,
        bool gtaVIsRunning,
        string? fiveMRoot,
        FiveMEdition edition,
        OptimizationProfile recommendedProfile) => new()
        {
            Edition = edition,
            IsFiveMRunning = isFiveMRunning,
            FiveMRoot = fiveMRoot,
            GtaVDetected = gtaVIsRunning,
            GtaVIsRunning = gtaVIsRunning,
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
            RecommendedProfile = recommendedProfile,
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

    public bool IncludesOptionalData { get; private set; }

    public int TrackCallCount { get; private set; }

    public void Configure(bool enabled, bool includeOptionalData)
    {
        IsEnabled = enabled;
        IncludesOptionalData = includeOptionalData;
    }

    public Task TrackAsync(AnonymousTelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
    {
        TrackCallCount++;
        return Task.CompletedTask;
    }

    public long SuccessfulSends => 0;
    public long FailedSends => 0;
    public bool IsHealthy => true;
}
