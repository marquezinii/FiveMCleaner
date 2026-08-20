using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Globalization;
using System.Windows.Threading;
using FiveMCleaner.App.Services;
using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Core.Planning;

namespace FiveMCleaner.App.ViewModels;

public sealed partial class MainViewModel : BindableBase, IDisposable
{
    private readonly IAppOptimizationService service;
    private readonly ILocalizationService localization;
    private readonly IStartupRegistrationService startupRegistration;
    private readonly IReleaseUpdateService? releaseUpdateService;
    private readonly ISilentUpdateInstaller? silentUpdateInstaller;
    private readonly IAnonymousTelemetryService telemetry;
    private readonly ILiveAlertService? liveAlertService;
    private readonly ILiveSystemMetricsProvider liveSystemMetricsProvider;
    private readonly ProgressTimingEstimator progressTimingEstimator = new();
    private readonly SemaphoreSlim settingsSaveGate = new(1, 1);
    private readonly Queue<string> pendingHeadlines = new();
    private static readonly TimeSpan HeadlineMinimumDwell = TimeSpan.FromSeconds(6);
    // Uma amostra por segundo: a leitura em si já leva ~300ms de janela PDH,
    // então cadências menores só se sobrepõem sem acrescentar informação.
    private static readonly TimeSpan LiveMetricsInterval = TimeSpan.FromSeconds(1);
    private const int LiveMetricsHistoryCapacity = 60;
    // Startup check plus this cadence is "almost instant" without polling the
    // free-tier Worker unnecessarily -- see
    // docs/superpowers/specs/2026-08-17-live-alerts-design.md.
    private static readonly TimeSpan LiveAlertPollInterval = TimeSpan.FromHours(1);
    private DispatcherTimer? headlineDwellTimer;
    private DateTime headlineShownAtUtc;
    private CancellationTokenSource? operationCancellation;
    private AppDiagnostic? diagnostic;
    private IReadOnlyList<AppHistoryRecord> historyRecords = [];
    private OptimizationPlanDto? currentPlan;
    private OptimizationProfile selectedProfile = OptimizationProfile.Balanced;
    private bool isBusy;
    private bool isInitializing = true;
    private double progressPercent;
    private string progressHeadline = string.Empty;
    private string previousProgressHeadline = string.Empty;
    private string elapsedTimeLabel = string.Empty;
    private string remainingTimeLabel = string.Empty;
    private string cpuName = string.Empty;
    private string ramLabel = string.Empty;
    private string diskLabel = string.Empty;
    private string windowsLabel = string.Empty;
    private string gpuDetail = string.Empty;
    private string readinessScoreExplanation = string.Empty;
    private string editionLabel = string.Empty;
    private string editionBadgeLabel = "AUTO";
    private string gtaStatusLabel = string.Empty;
    private bool isFiveMLegacyDetected;
    private bool isGtaVLegacyDetected;
    private string recommendationTitle = string.Empty;
    private string recommendationText = string.Empty;
    private string streamingReadinessTitle = string.Empty;
    private string streamingReadinessDetail = string.Empty;
    private string readinessLevelLabel = string.Empty;
    private string logicalProcessorLabel = string.Empty;
    private string logicalProcessorDetail = string.Empty;
    private string availableMemoryLabel = string.Empty;
    private string availableMemoryDetail = string.Empty;
    private string legacyCacheLabel = string.Empty;
    private string legacyCacheDetail = string.Empty;
    private string performancePressureLabel = string.Empty;
    private string performancePressureBrushKey = "TextTertiaryBrush";
    private string lastScanLabel = string.Empty;
    private string greetingTitle = string.Empty;
    private string? accountFirstName;
    private string lastOptimizationTitle = string.Empty;
    private string lastOptimizationDateLabel = string.Empty;
    private string lastOptimizationSummary = string.Empty;
    private bool hasLastOptimization;
    private string memoryUsageDetailLabel = string.Empty;
    private string cpuTrendLabel = string.Empty;
    private string gpuTrendLabel = string.Empty;
    private double cpuUsagePercent;
    private double gpuUsagePercent;
    private double memoryUsagePercent;
    private double diskUsagePercent;
    private string cpuUsageLabel = string.Empty;
    private string gpuUsageLabel = string.Empty;
    private string memoryUsageLabel = string.Empty;
    private string diskUsageLabel = string.Empty;
    private string networkUsageLabel = string.Empty;
    private string liveMetricsUpdatedLabel = string.Empty;
    private IReadOnlyList<double> cpuUsageSeries = [];
    private IReadOnlyList<double> gpuUsageSeries = [];
    private readonly Queue<double> cpuUsageHistory = new();
    private readonly Queue<double> gpuUsageHistory = new();
    private DispatcherTimer? liveMetricsTimer;
    private bool liveMetricsEnabled;
    private bool liveMetricsCaptureInProgress;
    private bool liveMetricsUnavailable;
    private LiveSystemMetricsSnapshot? lastLiveMetrics;
    private int readinessScore;
    private AppLanguagePreference languagePreference = AppLanguagePreference.Automatic;
    private AppThemePreference themePreference = AppThemePreference.System;
    private bool minimizeToTrayOnClose;
    private bool launchAtStartup;
    private bool checkForUpdates = true;
    private bool shareAnonymousTelemetry;
    private bool shareCrashReports;
    private int? privacyConsentVersion;
    private string? lastSeenReleaseNotesVersion;
    private ReleaseUpdate? availableUpdate;
    private UpdatePresentationState updatePresentationState;
    private string? updateFailureMessage;
    private bool isUpdateDownloading;
    private bool isInstallingUpdate;
    private double updateDownloadPercent;
    private string updateBannerTitle = string.Empty;
    private string updateBannerDetail = string.Empty;
    private bool isCheckingForUpdatesManually;
    private string? manualUpdateCheckMessage;
    private long settingsRevision;
    private bool profileInitializedFromDiagnostic;
    private Stopwatch? operationStopwatch;
    private DispatcherTimer? operationTimer;
    private OptimizationReportDto? lastReport;
    private string reportSummaryLabel = string.Empty;
    private string reportRestartLabel = string.Empty;
    private bool isReportAvailable;
    private string profilePresentationBenefits = string.Empty;
    private string profilePresentationImpact = string.Empty;
    private string profilePresentationCategories = string.Empty;
    private OptimizationComparisonResult? lastComparison;
    private Guid? lastTransactionId;
    private bool isComparisonAvailable;
    private bool comparisonRegressionSuspected;
    private string comparisonSummaryLabel = string.Empty;
    private string comparisonHardwareProfileLabel = string.Empty;
    private bool isGtaVBenchmarkRunning;
    private string gtaVBenchmarkStatusLabel = string.Empty;
    private DispatcherTimer? liveAlertTimer;
    private string? liveAlertId;
    private string? dismissedLiveAlertId;
    private bool isLiveAlertBannerVisible;
    private bool isLiveAlertIconVisible;
    private string liveAlertMessage = string.Empty;

    public MainViewModel(
        IAppOptimizationService service,
        ILocalizationService? localization = null,
        IStartupRegistrationService? startupRegistration = null,
        IReleaseUpdateService? releaseUpdateService = null,
        IAnonymousTelemetryService? telemetry = null,
        ISilentUpdateInstaller? silentUpdateInstaller = null,
        ILiveSystemMetricsProvider? liveSystemMetricsProvider = null,
        ILiveAlertService? liveAlertService = null)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.localization = localization ?? LocalizationService.Current;
        this.startupRegistration = startupRegistration ?? new WindowsStartupRegistrationService();
        this.releaseUpdateService = releaseUpdateService;
        this.silentUpdateInstaller = silentUpdateInstaller;
        this.telemetry = telemetry ?? DisabledAnonymousTelemetryService.Instance;
        this.liveAlertService = liveAlertService;
        this.liveSystemMetricsProvider = liveSystemMetricsProvider ?? new WindowsLiveSystemMetricsProvider();
        StepLedger.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasStepLedgerItems));
        ResetLocalizedPlaceholders();
        RefreshProfilePresentation();
        RefreshGreeting();
    }

    public ObservableCollection<ActionDisplayItem> PlannedActions { get; } = [];

    public ObservableCollection<HistoryDisplayItem> HistoryItems { get; } = [];

    public ObservableCollection<StreamingReadinessDisplayItem> StreamingReadinessItems { get; } = [];

    public ObservableCollection<StepLedgerItem> StepLedger { get; } = [];

    public ObservableCollection<ReportLineDisplayItem> ReportLines { get; } = [];

    public bool HasStepLedgerItems => StepLedger.Count > 0;

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                OnPropertyChanged(nameof(IsOptimizerIdle));
                RaiseCommandState();
            }
        }
    }

    public bool IsOptimizerIdle => !IsBusy && !IsReportAvailable;

    public bool CanRefresh => !IsBusy && !isInitializing;

    public bool CanStart => !IsBusy
        && !isInitializing
        && currentPlan?.IsExecutable == true
        && diagnostic?.IsFiveMRunning != true
        && diagnostic?.GtaVIsRunning != true;

    public bool CanCancel => IsBusy && operationCancellation is not null;

    public string LogsDirectory => service.LogsDirectory;

    public string AppVersion => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.2.0";

    public string AboutVersionDeveloper => localization.Format("About.VersionDeveloper", AppVersion);

    public async Task InitializeAsync()
    {
        isInitializing = true;
        RaiseCommandState();
        try
        {
            var settingsTask = service.LoadSettingsAsync();
            var diagnosticTask = service.DiagnoseAsync();
            var historyTask = service.LoadHistoryAsync();
            await Task.WhenAll(settingsTask, diagnosticTask, historyTask);

            var loadedSettings = await settingsTask;
            ApplySettings(loadedSettings);
            PrivacyConsentDecision = PrivacyConsentEvaluator.Evaluate(
                loadedSettings,
                service.SettingsFileExists());
            PendingReleaseNotes = ReleaseNotesEvaluator.Evaluate(
                loadedSettings,
                service.SettingsFileExists(),
                AppVersion,
                ReleaseNotesCatalog.Versions);
            ApplyDiagnostic(await diagnosticTask);
            ApplyHistory(await historyTask);
            if (checkForUpdates && releaseUpdateService is not null)
            {
                _ = CheckForUpdatesAsync().ContinueWith(
                    static t => { _ = t.Exception; },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            if (liveAlertService is not null)
            {
                _ = CheckLiveAlertAsync().ContinueWith(
                    static t => { _ = t.Exception; },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);

                liveAlertTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = LiveAlertPollInterval };
                liveAlertTimer.Tick += (_, _) => _ = CheckLiveAlertAsync();
                liveAlertTimer.Start();
            }
        }
        catch (Exception exception)
        {
            RecommendationTitle = localization.GetString("Diagnosis.Partial");
            RecommendationText = localization.DescribeException(exception);
        }
        finally
        {
            isInitializing = false;
            RefreshPlan();
            RaiseCommandState();
        }
    }

    public async Task RefreshDiagnosticAsync()
    {
        if (!CanRefresh)
        {
            return;
        }

        isInitializing = true;
        RaiseCommandState();
        try
        {
            ApplyDiagnostic(await service.DiagnoseAsync());
        }
        catch (Exception exception)
        {
            RecommendationTitle = localization.GetString("Diagnosis.CouldNotScanAgain");
            RecommendationText = localization.DescribeException(exception);
        }
        finally
        {
            isInitializing = false;
            RefreshPlan();
            RaiseCommandState();
        }
    }

    private void RaiseCommandState()
    {
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanRevertLastOptimization));
        OnPropertyChanged(nameof(CanRunGtaVBenchmark));
        // Updating restarts the app, so the button has to follow IsBusy.
        OnPropertyChanged(nameof(CanDownloadUpdate));
    }

    public void Dispose()
    {
        liveMetricsEnabled = false;
        liveMetricsTimer?.Stop();
        liveMetricsTimer = null;
        liveAlertTimer?.Stop();
        liveAlertTimer = null;
        (liveSystemMetricsProvider as IDisposable)?.Dispose();
    }
}
