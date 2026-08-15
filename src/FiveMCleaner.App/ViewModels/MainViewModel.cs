using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using FiveMCleaner.App.Services;
using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Core.Planning;

namespace FiveMCleaner.App.ViewModels;

public sealed class MainViewModel : BindableBase, IDisposable
{
    private readonly IAppOptimizationService service;
    private readonly ILocalizationService localization;
    private readonly IStartupRegistrationService startupRegistration;
    private readonly IReleaseUpdateService? releaseUpdateService;
    private readonly ISilentUpdateInstaller? silentUpdateInstaller;
    private readonly IAnonymousTelemetryService telemetry;
    private readonly ILiveSystemMetricsProvider liveSystemMetricsProvider;
    private readonly ProgressTimingEstimator progressTimingEstimator = new();
    private readonly SemaphoreSlim settingsSaveGate = new(1, 1);
    private readonly Queue<string> pendingHeadlines = new();
    private static readonly TimeSpan HeadlineMinimumDwell = TimeSpan.FromSeconds(6);
    // Uma amostra por segundo: a leitura em si já leva ~300ms de janela PDH,
    // então cadências menores só se sobrepõem sem acrescentar informação.
    private static readonly TimeSpan LiveMetricsInterval = TimeSpan.FromSeconds(1);
    private const int LiveMetricsHistoryCapacity = 60;
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
    private string performancePressureBrushKey = "TextMutedBrush";
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

    public MainViewModel(
        IAppOptimizationService service,
        ILocalizationService? localization = null,
        IStartupRegistrationService? startupRegistration = null,
        IReleaseUpdateService? releaseUpdateService = null,
        IAnonymousTelemetryService? telemetry = null,
        ISilentUpdateInstaller? silentUpdateInstaller = null,
        ILiveSystemMetricsProvider? liveSystemMetricsProvider = null)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.localization = localization ?? LocalizationService.Current;
        this.startupRegistration = startupRegistration ?? new WindowsStartupRegistrationService();
        this.releaseUpdateService = releaseUpdateService;
        this.silentUpdateInstaller = silentUpdateInstaller;
        this.telemetry = telemetry ?? DisabledAnonymousTelemetryService.Instance;
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

    public string ReportSummaryLabel { get => reportSummaryLabel; private set => SetProperty(ref reportSummaryLabel, value); }

    public string ReportRestartLabel { get => reportRestartLabel; private set => SetProperty(ref reportRestartLabel, value); }

    public bool IsReportAvailable
    {
        get => isReportAvailable;
        private set
        {
            if (SetProperty(ref isReportAvailable, value))
            {
                OnPropertyChanged(nameof(IsOptimizerIdle));
            }
        }
    }

    public bool IsComparisonAvailable { get => isComparisonAvailable; private set => SetProperty(ref isComparisonAvailable, value); }

    public bool ComparisonRegressionSuspected { get => comparisonRegressionSuspected; private set => SetProperty(ref comparisonRegressionSuspected, value); }

    public string ComparisonSummaryLabel { get => comparisonSummaryLabel; private set => SetProperty(ref comparisonSummaryLabel, value); }

    public string ComparisonHardwareProfileLabel { get => comparisonHardwareProfileLabel; private set => SetProperty(ref comparisonHardwareProfileLabel, value); }

    public bool CanRevertLastOptimization => ComparisonRegressionSuspected
        && !IsBusy
        && lastTransactionId is { } id
        && HistoryItems.Any(item => item.TransactionId == id && item.CanRollback);

    public bool IsGtaVBenchmarkRunning { get => isGtaVBenchmarkRunning; private set => SetProperty(ref isGtaVBenchmarkRunning, value); }

    public string GtaVBenchmarkStatusLabel { get => gtaVBenchmarkStatusLabel; private set => SetProperty(ref gtaVBenchmarkStatusLabel, value); }

    public bool CanRunGtaVBenchmark => !IsBusy && !IsGtaVBenchmarkRunning;

    public string ProfilePresentationBenefits { get => profilePresentationBenefits; private set => SetProperty(ref profilePresentationBenefits, value); }

    public string ProfilePresentationImpact { get => profilePresentationImpact; private set => SetProperty(ref profilePresentationImpact, value); }

    public string ProfilePresentationCategories { get => profilePresentationCategories; private set => SetProperty(ref profilePresentationCategories, value); }

    public string CpuName { get => cpuName; private set => SetProperty(ref cpuName, value); }

    public string RamLabel { get => ramLabel; private set => SetProperty(ref ramLabel, value); }

    public string DiskLabel { get => diskLabel; private set => SetProperty(ref diskLabel, value); }

    public string WindowsLabel { get => windowsLabel; private set => SetProperty(ref windowsLabel, value); }

    public string GpuDetail { get => gpuDetail; private set => SetProperty(ref gpuDetail, value); }

    public string ReadinessScoreExplanation { get => readinessScoreExplanation; private set => SetProperty(ref readinessScoreExplanation, value); }

    public string EditionLabel { get => editionLabel; private set => SetProperty(ref editionLabel, value); }

    public string EditionBadgeLabel { get => editionBadgeLabel; private set => SetProperty(ref editionBadgeLabel, value); }

    public string GtaStatusLabel { get => gtaStatusLabel; private set => SetProperty(ref gtaStatusLabel, value); }

    public bool IsFiveMLegacyDetected { get => isFiveMLegacyDetected; private set => SetProperty(ref isFiveMLegacyDetected, value); }

    public bool IsGtaVLegacyDetected { get => isGtaVLegacyDetected; private set => SetProperty(ref isGtaVLegacyDetected, value); }

    public string RecommendationTitle { get => recommendationTitle; private set => SetProperty(ref recommendationTitle, value); }

    public string RecommendationText { get => recommendationText; private set => SetProperty(ref recommendationText, value); }

    public string StreamingReadinessTitle { get => streamingReadinessTitle; private set => SetProperty(ref streamingReadinessTitle, value); }

    public string StreamingReadinessDetail { get => streamingReadinessDetail; private set => SetProperty(ref streamingReadinessDetail, value); }

    public string ReadinessLevelLabel { get => readinessLevelLabel; private set => SetProperty(ref readinessLevelLabel, value); }

    /// <summary>Logical processor count reported by the local scan, as a bare number.</summary>
    public string LogicalProcessorLabel { get => logicalProcessorLabel; private set => SetProperty(ref logicalProcessorLabel, value); }

    public string LogicalProcessorDetail { get => logicalProcessorDetail; private set => SetProperty(ref logicalProcessorDetail, value); }

    /// <summary>Free physical memory at scan time (e.g. "12,4 GB").</summary>
    public string AvailableMemoryLabel { get => availableMemoryLabel; private set => SetProperty(ref availableMemoryLabel, value); }

    public string AvailableMemoryDetail { get => availableMemoryDetail; private set => SetProperty(ref availableMemoryDetail, value); }

    /// <summary>
    /// Size of the FiveM server cache found on disk. This is the single number
    /// that most often explains why the optimizer has something to do, so the
    /// overview shows it instead of leaving the user to guess.
    /// </summary>
    public string LegacyCacheLabel { get => legacyCacheLabel; private set => SetProperty(ref legacyCacheLabel, value); }

    public string LegacyCacheDetail { get => legacyCacheDetail; private set => SetProperty(ref legacyCacheDetail, value); }

    public string PerformancePressureLabel { get => performancePressureLabel; private set => SetProperty(ref performancePressureLabel, value); }

    public string PerformancePressureBrushKey { get => performancePressureBrushKey; private set => SetProperty(ref performancePressureBrushKey, value); }

    /// <summary>When the last local scan finished, already localized.</summary>
    public string LastScanLabel { get => lastScanLabel; private set => SetProperty(ref lastScanLabel, value); }

    /// <summary>
    /// "Boa tarde, Felipe. O que iremos fazer hoje?" — greets by local time of
    /// day, with the first name only when a session is signed in and the
    /// account has one on file. See <see cref="RefreshGreeting"/>.
    /// </summary>
    public string GreetingTitle { get => greetingTitle; private set => SetProperty(ref greetingTitle, value); }

    public string LastOptimizationTitle { get => lastOptimizationTitle; private set => SetProperty(ref lastOptimizationTitle, value); }

    public string LastOptimizationDateLabel { get => lastOptimizationDateLabel; private set => SetProperty(ref lastOptimizationDateLabel, value); }

    public string LastOptimizationSummary { get => lastOptimizationSummary; private set => SetProperty(ref lastOptimizationSummary, value); }

    /// <summary>False when this machine has never completed an optimization.</summary>
    public bool HasLastOptimization { get => hasLastOptimization; private set => SetProperty(ref hasLastOptimization, value); }

    public double CpuUsagePercent { get => cpuUsagePercent; private set => SetProperty(ref cpuUsagePercent, value); }

    public double GpuUsagePercent { get => gpuUsagePercent; private set => SetProperty(ref gpuUsagePercent, value); }

    public double MemoryUsagePercent { get => memoryUsagePercent; private set => SetProperty(ref memoryUsagePercent, value); }

    public double DiskUsagePercent { get => diskUsagePercent; private set => SetProperty(ref diskUsagePercent, value); }

    public string CpuUsageLabel { get => cpuUsageLabel; private set => SetProperty(ref cpuUsageLabel, value); }

    public string GpuUsageLabel { get => gpuUsageLabel; private set => SetProperty(ref gpuUsageLabel, value); }

    public string MemoryUsageLabel { get => memoryUsageLabel; private set => SetProperty(ref memoryUsageLabel, value); }

    public string DiskUsageLabel { get => diskUsageLabel; private set => SetProperty(ref diskUsageLabel, value); }

    public string NetworkUsageLabel { get => networkUsageLabel; private set => SetProperty(ref networkUsageLabel, value); }

    /// <summary>Live memory reading in absolute terms (e.g. "12,4 / 31,9 GB").</summary>
    public string MemoryUsageDetailLabel { get => memoryUsageDetailLabel; private set => SetProperty(ref memoryUsageDetailLabel, value); }

    /// <summary>Average and peak CPU over the samples currently plotted.</summary>
    public string CpuTrendLabel { get => cpuTrendLabel; private set => SetProperty(ref cpuTrendLabel, value); }

    /// <summary>Average and peak GPU over the samples currently plotted.</summary>
    public string GpuTrendLabel { get => gpuTrendLabel; private set => SetProperty(ref gpuTrendLabel, value); }

    public string LiveMetricsUpdatedLabel { get => liveMetricsUpdatedLabel; private set => SetProperty(ref liveMetricsUpdatedLabel, value); }

    /// <summary>
    /// Histórico de CPU em porcentagem, da amostra mais antiga para a mais
    /// recente. A cena 3D da Visão geral consome os valores crus e cuida da
    /// projeção; o modelo não conhece geometria de tela.
    /// </summary>
    public IReadOnlyList<double> CpuUsageSeries { get => cpuUsageSeries; private set => SetProperty(ref cpuUsageSeries, value); }

    /// <summary>Histórico de GPU em porcentagem, na mesma ordem de <see cref="CpuUsageSeries"/>.</summary>
    public IReadOnlyList<double> GpuUsageSeries { get => gpuUsageSeries; private set => SetProperty(ref gpuUsageSeries, value); }

    public int ReadinessScore { get => readinessScore; private set => SetProperty(ref readinessScore, value); }

    public double ProgressPercent
    {
        get => progressPercent;
        private set
        {
            if (SetProperty(ref progressPercent, value))
            {
                OnPropertyChanged(nameof(ProgressIntensity));
            }
        }
    }

    /// <summary>
    /// Progresso real da execução mapeado para a faixa 0,3–1, que a cena 3D do
    /// Otimizador usa como velocidade e brilho. Não é uma medida nova: é o
    /// mesmo <see cref="ProgressPercent"/>, com um piso para que o núcleo nunca
    /// pareça parado nos primeiros segundos de uma execução que já começou.
    /// </summary>
    public double ProgressIntensity => 0.3 + (Math.Clamp(ProgressPercent / 100d, 0, 1) * 0.7);

    public string ProgressHeadline
    {
        get => progressHeadline;
        private set
        {
            if (!string.Equals(progressHeadline, value, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(progressHeadline))
            {
                PreviousProgressHeadline = progressHeadline;
            }

            SetProperty(ref progressHeadline, value);
        }
    }

    public string PreviousProgressHeadline
    {
        get => previousProgressHeadline;
        private set
        {
            if (SetProperty(ref previousProgressHeadline, value))
            {
                OnPropertyChanged(nameof(HasPreviousProgressHeadline));
            }
        }
    }

    public bool HasPreviousProgressHeadline => !string.IsNullOrWhiteSpace(PreviousProgressHeadline);

    public string ElapsedTimeLabel { get => elapsedTimeLabel; private set => SetProperty(ref elapsedTimeLabel, value); }

    public string RemainingTimeLabel { get => remainingTimeLabel; private set => SetProperty(ref remainingTimeLabel, value); }

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

    public bool IsLightSelected
    {
        get => selectedProfile == OptimizationProfile.Light;
        set { if (value) SelectProfile(OptimizationProfile.Light); }
    }

    public bool IsBalancedSelected
    {
        get => selectedProfile == OptimizationProfile.Balanced;
        set { if (value) SelectProfile(OptimizationProfile.Balanced); }
    }

    public bool IsAggressiveSelected
    {
        get => selectedProfile == OptimizationProfile.Aggressive;
        set { if (value) SelectProfile(OptimizationProfile.Aggressive); }
    }

    private OptimizationProfile RecommendedProfile =>
        diagnostic?.RecommendedProfile ?? OptimizationProfile.Balanced;

    public bool IsLightRecommended => RecommendedProfile == OptimizationProfile.Light;

    public bool IsBalancedRecommended => RecommendedProfile == OptimizationProfile.Balanced;

    public bool IsAggressiveRecommended => RecommendedProfile == OptimizationProfile.Aggressive;

    public AppThemePreference ThemePreference => themePreference;

    public AppLanguagePreference LanguagePreference => languagePreference;

    public AppLanguage CurrentLanguage => localization.CurrentLanguage;

    public bool IsEnglishSelected => CurrentLanguage == AppLanguage.English;

    public bool IsPortugueseSelected => CurrentLanguage == AppLanguage.PortugueseBrazil;

    public bool IsSpanishSelected => CurrentLanguage == AppLanguage.Spanish;

    public bool IsCloseAppOnCloseSelected
    {
        get => !MinimizeToTrayOnClose;
        set
        {
            if (value)
            {
                MinimizeToTrayOnClose = false;
            }
        }
    }

    public bool IsMinimizeToTrayOnCloseSelected
    {
        get => MinimizeToTrayOnClose;
        set
        {
            if (value)
            {
                MinimizeToTrayOnClose = true;
            }
        }
    }

    public bool IsSystemThemeSelected => themePreference == AppThemePreference.System;

    public bool IsDarkThemeSelected => themePreference == AppThemePreference.Dark;

    public bool IsLightThemeSelected => themePreference == AppThemePreference.Light;

    public bool MinimizeToTrayOnClose
    {
        get => minimizeToTrayOnClose;
        set
        {
            if (SetProperty(ref minimizeToTrayOnClose, value))
            {
                OnPropertyChanged(nameof(IsCloseAppOnCloseSelected));
                OnPropertyChanged(nameof(IsMinimizeToTrayOnCloseSelected));
                SettingsChanged(refreshPlan: false);
            }
        }
    }

    public bool LaunchAtStartup
    {
        get => launchAtStartup;
        set
        {
            if (launchAtStartup == value)
            {
                return;
            }

            try
            {
                startupRegistration.SetEnabled(value);
                launchAtStartup = value;
                OnPropertyChanged();
                SettingsChanged(refreshPlan: false);
            }
            catch (Exception)
            {
                OnPropertyChanged();
            }
        }
    }

    public bool CheckForUpdates
    {
        get => checkForUpdates;
        set
        {
            if (SetProperty(ref checkForUpdates, value))
            {
                SettingsChanged(refreshPlan: false);
            }
        }
    }

    public bool ShareAnonymousTelemetry
    {
        get => shareAnonymousTelemetry;
        set
        {
            if (SetProperty(ref shareAnonymousTelemetry, value))
            {
                telemetry.SetEnabled(value);
                SettingsChanged(refreshPlan: false);
            }
        }
    }

    /// <summary>
    /// Consentimento para relatórios automáticos de falhas. Alterar este
    /// toggle nas configurações persiste imediatamente pelo mesmo mecanismo
    /// já usado pelos demais ajustes, mas nunca altera
    /// <see cref="PrivacyConsentVersion"/> nem reabre a tela de
    /// consentimento — só a confirmação explícita dessa tela faz isso (ver
    /// <see cref="ConfirmPrivacyConsentAsync"/>). Nenhum serviço externo de
    /// relatório de falhas existe ainda; este toggle só governa a
    /// preferência persistida.
    /// </summary>
    public bool ShareCrashReports
    {
        get => shareCrashReports;
        set
        {
            if (SetProperty(ref shareCrashReports, value))
            {
                SettingsChanged(refreshPlan: false);
            }
        }
    }

    /// <summary>
    /// Decisão computada pelo <see cref="PrivacyConsentEvaluator"/> a partir
    /// das configurações recém-carregadas em <see cref="InitializeAsync"/>.
    /// <see langword="null"/> antes da primeira inicialização. A janela
    /// (responsabilidade da view) decide se e qual variante mostrar a partir
    /// deste valor; nenhuma leitura adicional de <c>settings.json</c> é
    /// necessária para isso.
    /// </summary>
    public PrivacyConsentDecision? PrivacyConsentDecision { get; private set; }

    public bool IsUpdateBannerVisible => availableUpdate is not null
        || updatePresentationState == UpdatePresentationState.Failed
        || JustUpdatedToVersion is not null;

    public bool IsUpdateDownloading
    {
        get => isUpdateDownloading;
        private set
        {
            if (SetProperty(ref isUpdateDownloading, value))
            {
                OnPropertyChanged(nameof(CanDownloadUpdate));
            }
        }
    }

    public bool IsInstallingUpdate
    {
        get => isInstallingUpdate;
        private set
        {
            if (SetProperty(ref isInstallingUpdate, value))
            {
                OnPropertyChanged(nameof(CanDownloadUpdate));
            }
        }
    }

    /// <summary>
    /// Updating replaces the running executable and restarts the app. Doing
    /// that while a transaction is applying changes, rolling back or writing
    /// its journal would abandon the operation halfway with no way to finish
    /// or revert it, so the update button stays disabled until the app is idle.
    /// </summary>
    public bool CanDownloadUpdate => availableUpdate is not null
        && !IsUpdateDownloading
        && !IsInstallingUpdate
        && !IsBusy;

    /// <summary>
    /// Non-null only on the launch that immediately follows a successful
    /// automatic update (the installer relaunches the app with
    /// <c>--updated=X.Y.Z</c>), so the banner can confirm what happened.
    /// </summary>
    public string? JustUpdatedToVersion { get; private set; }

    public Uri? ReleaseNotesUri => availableUpdate?.ReleaseNotesUri;

    /// <summary>Core version string (e.g. "1.2.3") of the pending update, for
    /// the one confirmation dialog shown before the silent install starts.</summary>
    public string? AvailableUpdateVersion => availableUpdate?.Version.CoreVersion;

    public bool CanOpenReleaseNotes => ReleaseNotesUri is not null;

    public double UpdateDownloadPercent
    {
        get => updateDownloadPercent;
        private set => SetProperty(ref updateDownloadPercent, value);
    }

    public string UpdateBannerTitle
    {
        get => updateBannerTitle;
        private set => SetProperty(ref updateBannerTitle, value);
    }

    public string UpdateBannerDetail
    {
        get => updateBannerDetail;
        private set => SetProperty(ref updateBannerDetail, value);
    }

    /// <summary>
    /// The post-update confirmation banner has nothing left to act on, so the
    /// action button disappears instead of offering a redundant update.
    /// </summary>
    public bool IsUpdateActionVisible => JustUpdatedToVersion is null;

    /// <summary>
    /// True on the launch that follows a successful automatic update, when the
    /// confirmation banner can be dismissed once the user has read it.
    /// </summary>
    public bool IsUpdateCompletedBannerVisible => JustUpdatedToVersion is not null;

    /// <summary>
    /// One button, one meaning: the whole update is a single click. It only
    /// changes wording to reflect a retry after a failure.
    /// </summary>
    public string UpdateActionLabel => localization.GetString(
        updatePresentationState == UpdatePresentationState.Failed
            ? "Common.Retry"
            : "Update.InstallNow");

    public string UpdateReleaseNotesLabel => localization.GetString("Update.ReleaseNotes");

    public bool IsCheckingForUpdatesManually
    {
        get => isCheckingForUpdatesManually;
        private set
        {
            if (SetProperty(ref isCheckingForUpdatesManually, value))
            {
                OnPropertyChanged(nameof(CanCheckForUpdatesManually));
            }
        }
    }

    public bool CanCheckForUpdatesManually => !IsCheckingForUpdatesManually;

    public string? ManualUpdateCheckMessage
    {
        get => manualUpdateCheckMessage;
        private set => SetProperty(ref manualUpdateCheckMessage, value);
    }

    public int SelectedActionCount => currentPlan?.Actions.Count ?? 0;

    public string ElevationLabel => localization.GetString(
        currentPlan?.RequiresElevation == true
            ? "Plan.Elevation.UacAtRun"
            : "Plan.Elevation.None");

    public string PlanSummary => currentPlan?.ContainsNonReversibleActions == true
        ? localization.GetString("Plan.Safety.Mixed")
        : localization.GetString("Plan.Safety.Reversible");

    public string PlanHeader => localization.Format(
        "Plan.ActionsCatalog",
        SelectedActionCount,
        currentPlan?.CatalogVersion ?? 1);

    public string PlanNoticesText => currentPlan?.Notices.Count > 0
        ? string.Join("  •  ", currentPlan.Notices.Select(LocalizeNotice))
        : localization.GetString("Plan.NoAdditionalWarnings");

    public string SelectedProfileLabel
    {
        get
        {
            var upper = SelectedProfileName.ToUpper(localization.CurrentCulture);
            return selectedProfile == RecommendedProfile
                ? $"{upper} • {localization.GetString("Profiles.RecommendedBadge")}"
                : upper;
        }
    }

    /// <summary>
    /// True when the "Recomendado" mark should render as its own badge next
    /// to <see cref="SelectedProfileName"/>, instead of text concatenated
    /// into the all-caps <see cref="SelectedProfileLabel"/> heading.
    /// </summary>
    public bool IsSelectedProfileRecommended => selectedProfile == RecommendedProfile;

    /// <summary>
    /// Posição do perfil selecionado na escala Leve → Médio → Agressivo, de 0 a 1.
    /// Não é uma estimativa de ganho nem uma medida de FPS: é só o nível
    /// escolhido, exposto para que a cena 3D e o anel do Otimizador reajam de
    /// forma visível quando o usuário troca de perfil.
    /// </summary>
    public double ProfileIntensity => selectedProfile switch
    {
        OptimizationProfile.Light => 0.34,
        OptimizationProfile.Aggressive => 1,
        _ => 0.67
    };

    public double ProfileIntensityPercent => ProfileIntensity * 100;

    public string SafetySummary => currentPlan?.RequiresElevation == true
        ? localization.GetString("Plan.Elevation.OnePrompt")
        : localization.GetString("Plan.Elevation.CurrentUser");

    public string LogsDirectory => service.LogsDirectory;

    public string AppVersion => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.2.0";

    public string AboutVersionDeveloper => localization.Format("About.VersionDeveloper", AppVersion);

    public string SelectedProfileName => ProfileName(selectedProfile);

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

    /// <summary>
    /// Verdadeiro enquanto a Visão geral está ativa e coletando. A cena 3D usa
    /// esse estado para parar de animar quando a página sai de cena ou a janela
    /// vai para a bandeja, em vez de girar sem ninguém olhando.
    /// </summary>
    public bool IsLiveMetricsActive
    {
        get => liveMetricsEnabled;
        private set => SetProperty(ref liveMetricsEnabled, value);
    }

    /// <summary>
    /// Estado real do bloco "Desempenho ao vivo": a pílula, o gráfico e o
    /// rótulo de atualização precisam concordar entre si em vez de a pílula
    /// dizer "AO VIVO" enquanto os valores ainda leem "Lendo..." ou falharam.
    /// </summary>
    public bool IsLivePerformanceLive => liveMetricsEnabled && lastLiveMetrics is not null && !liveMetricsUnavailable;

    public bool IsLivePerformanceWaiting => liveMetricsEnabled && lastLiveMetrics is null && !liveMetricsUnavailable;

    public bool IsLivePerformanceUnavailable => liveMetricsEnabled && liveMetricsUnavailable;

    public bool HasLiveMetricsSample => lastLiveMetrics is not null;

    private void NotifyLivePerformanceStateChanged()
    {
        OnPropertyChanged(nameof(IsLivePerformanceLive));
        OnPropertyChanged(nameof(IsLivePerformanceWaiting));
        OnPropertyChanged(nameof(IsLivePerformanceUnavailable));
        OnPropertyChanged(nameof(HasLiveMetricsSample));
    }

    public void SetLiveMetricsEnabled(bool enabled)
    {
        IsLiveMetricsActive = enabled;
        NotifyLivePerformanceStateChanged();
        if (!enabled)
        {
            liveMetricsTimer?.Stop();
            return;
        }

        // A saudação só é recalculada aqui (ao reabrir a Visão geral), não em
        // um timer próprio: ela muda no máximo três vezes por dia, então não
        // vale a pena um relógio dedicado para isso.
        RefreshGreeting();

        liveMetricsTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = LiveMetricsInterval
        };
        liveMetricsTimer.Tick -= LiveMetricsTimer_Tick;
        liveMetricsTimer.Tick += LiveMetricsTimer_Tick;
        liveMetricsTimer.Start();
        _ = CaptureLiveMetricsAsync();
    }

    private void LiveMetricsTimer_Tick(object? sender, EventArgs e) => _ = CaptureLiveMetricsAsync();

    private async Task CaptureLiveMetricsAsync()
    {
        if (!liveMetricsEnabled || liveMetricsCaptureInProgress)
        {
            return;
        }

        liveMetricsCaptureInProgress = true;
        try
        {
            var snapshot = await liveSystemMetricsProvider.CaptureAsync();
            if (!liveMetricsEnabled)
            {
                return;
            }

            lastLiveMetrics = snapshot;
            liveMetricsUnavailable = false;
            ApplyLiveMetrics(snapshot);
            NotifyLivePerformanceStateChanged();
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            if (liveMetricsEnabled)
            {
                liveMetricsUnavailable = true;
                LiveMetricsUpdatedLabel = localization.GetString("Dashboard.LivePerformance.Unavailable");
                NotifyLivePerformanceStateChanged();
            }
        }
        finally
        {
            liveMetricsCaptureInProgress = false;
        }
    }

    private void ApplyLiveMetrics(LiveSystemMetricsSnapshot snapshot, bool addHistory = true)
    {
        CpuUsagePercent = snapshot.CpuPercent ?? 0;
        GpuUsagePercent = snapshot.GpuPercent ?? 0;
        MemoryUsagePercent = snapshot.MemoryPercent ?? 0;
        DiskUsagePercent = snapshot.DiskPercent ?? 0;
        CpuUsageLabel = FormatLivePercent(snapshot.CpuPercent);
        GpuUsageLabel = FormatLivePercent(snapshot.GpuPercent);
        MemoryUsageLabel = FormatLivePercent(snapshot.MemoryPercent);
        DiskUsageLabel = FormatLivePercent(snapshot.DiskPercent);
        NetworkUsageLabel = localization.Format(
            "Dashboard.LivePerformance.NetworkValue",
            snapshot.NetworkThroughputMBps);
        MemoryUsageDetailLabel = snapshot is { UsedMemoryGiB: { } used, TotalMemoryGiB: { } total }
            ? localization.Format("Dashboard.LivePerformance.MemoryDetail", used, total)
            : string.Empty;
        LiveMetricsUpdatedLabel = localization.Format(
            "Dashboard.LivePerformance.Updated",
            snapshot.CapturedAt.ToLocalTime().ToString("HH:mm:ss"));

        if (addHistory)
        {
            AddMetricSample(cpuUsageHistory, snapshot.CpuPercent);
            AddMetricSample(gpuUsageHistory, snapshot.GpuPercent);
            CpuUsageSeries = cpuUsageHistory.ToArray();
            GpuUsageSeries = gpuUsageHistory.ToArray();
        }

        CpuTrendLabel = DescribeTrend(cpuUsageHistory);
        GpuTrendLabel = DescribeTrend(gpuUsageHistory);
    }

    /// <summary>
    /// Average and peak of the samples currently plotted. Both come from the
    /// same history the chart draws, so the summary never contradicts the line
    /// above it; an empty history reports no reading instead of "0%".
    /// </summary>
    private string DescribeTrend(Queue<double> history)
    {
        if (history.Count == 0)
        {
            return localization.GetString("Dashboard.LivePerformance.NotAvailable");
        }

        return localization.Format(
            "Dashboard.LivePerformance.TrendValue",
            history.Average(),
            history.Max());
    }

    private string FormatLivePercent(double? value) => value is { } available
        ? localization.Format("Dashboard.LivePerformance.PercentValue", available)
        : localization.GetString("Dashboard.LivePerformance.NotAvailable");

    private static void AddMetricSample(Queue<double> history, double? value)
    {
        if (value is null)
        {
            return;
        }

        history.Enqueue(Math.Clamp(value.Value, 0, 100));
        while (history.Count > LiveMetricsHistoryCapacity)
        {
            history.Dequeue();
        }
    }

    /// <summary>
    /// Raised exactly once, right when a newer version is first detected,
    /// carrying the new version's core string (e.g. "1.2.3"). The main
    /// window subscribes to this to show the native Windows notification
    /// regardless of whether the window is currently in the foreground or
    /// minimized to the tray.
    /// </summary>
    public event EventHandler<string>? UpdateAvailableDetected;

    public async Task CheckForUpdatesAsync()
    {
        if (releaseUpdateService is null || availableUpdate is not null)
        {
            return;
        }

        try
        {
            var update = await releaseUpdateService.CheckForUpdateAsync(
                StableSemanticVersion.FromVersion(GetAssemblyVersion()));
            if (update is null)
            {
                return;
            }

            ApplyDetectedUpdate(update);
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            // Falha de rede na inicialização não interrompe diagnóstico nem otimização.
        }
    }

    /// <summary>
    /// Explicit "Procurar atualizações" entry point from Settings. Unlike
    /// <see cref="CheckForUpdatesAsync"/> (silent, startup-only, no-ops once
    /// an update was already found), this always performs a fresh check and
    /// always reports an outcome -- either the existing update banner, or an
    /// explicit "already on the latest version" message.
    /// </summary>
    public async Task CheckForUpdatesManuallyAsync()
    {
        if (releaseUpdateService is null || IsCheckingForUpdatesManually)
        {
            return;
        }

        IsCheckingForUpdatesManually = true;
        ManualUpdateCheckMessage = null;

        try
        {
            var update = await releaseUpdateService.CheckForUpdateAsync(
                StableSemanticVersion.FromVersion(GetAssemblyVersion()));

            if (update is null)
            {
                ManualUpdateCheckMessage = localization.GetString("Update.ManualCheck.UpToDate");
                return;
            }

            ApplyDetectedUpdate(update);
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            var message = localization.DescribeException(exception);
            ManualUpdateCheckMessage = localization.Format("Update.ManualCheck.Failed", message);
        }
        finally
        {
            IsCheckingForUpdatesManually = false;
        }
    }

    private static Version GetAssemblyVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    private void ApplyDetectedUpdate(ReleaseUpdate update)
    {
        availableUpdate = update;
        updatePresentationState = UpdatePresentationState.Available;
        RefreshUpdatePresentation();
        UpdateAvailableDetected?.Invoke(this, update.Version.CoreVersion);
    }

    public async Task<DownloadedUpdate?> DownloadAvailableUpdateAsync()
    {
        if (releaseUpdateService is null || availableUpdate is null || IsUpdateDownloading)
        {
            return null;
        }

        IsUpdateDownloading = true;
        updatePresentationState = UpdatePresentationState.Downloading;
        UpdateDownloadPercent = 0;
        RefreshUpdatePresentation();
        var progress = new Progress<UpdateDownloadProgress>(value =>
        {
            UpdateDownloadPercent = value.Percentage;
            RefreshUpdatePresentation();
        });

        try
        {
            var downloaded = await releaseUpdateService.DownloadUpdateAsync(
                availableUpdate,
                progress);
            UpdateDownloadPercent = 100;
            updatePresentationState = UpdatePresentationState.Ready;
            RefreshUpdatePresentation();
            return downloaded;
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            updateFailureMessage = localization.DescribeException(exception);
            updatePresentationState = UpdatePresentationState.Failed;
            RefreshUpdatePresentation();
            return null;
        }
        finally
        {
            IsUpdateDownloading = false;
        }
    }

    /// <summary>
    /// The whole one-click update: download the verified installer, then run it
    /// silently. Returns <see langword="true"/> only when the installer is
    /// actually running and the caller must now close the app so its files can
    /// be replaced — the installer reopens the new version by itself. Any
    /// failure returns <see langword="false"/> with the banner already
    /// explaining what happened, and the app must stay open.
    /// </summary>
    public async Task<bool> DownloadAndInstallUpdateAsync()
    {
        if (!CanDownloadUpdate)
        {
            return false;
        }

        var downloaded = await DownloadAvailableUpdateAsync().ConfigureAwait(true);
        if (downloaded is null)
        {
            return false;
        }

        return await InstallDownloadedUpdateAsync(downloaded).ConfigureAwait(true);
    }

    /// <summary>
    /// Runs an already downloaded and hash-verified installer in silent mode.
    /// Kept separate from the download so a retry does not re-download an
    /// installer that is already on disk and already verified.
    /// </summary>
    public async Task<bool> InstallDownloadedUpdateAsync(DownloadedUpdate downloaded)
    {
        ArgumentNullException.ThrowIfNull(downloaded);
        if (silentUpdateInstaller is null || IsInstallingUpdate || IsBusy)
        {
            return false;
        }

        IsInstallingUpdate = true;
        updatePresentationState = UpdatePresentationState.Installing;
        RefreshUpdatePresentation();

        try
        {
            var launch = await silentUpdateInstaller
                .StartAsync(downloaded)
                .ConfigureAwait(true);
            if (launch.Started)
            {
                return true;
            }

            updateFailureMessage = localization.GetString("Error.Unexpected");
            updatePresentationState = UpdatePresentationState.Failed;
            RefreshUpdatePresentation();
            return false;
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            updateFailureMessage = localization.DescribeException(exception);
            updatePresentationState = UpdatePresentationState.Failed;
            RefreshUpdatePresentation();
            return false;
        }
        finally
        {
            IsInstallingUpdate = false;
        }
    }

    /// <summary>
    /// Called at startup when the app was relaunched by its own installer after
    /// an automatic update. Shows the confirmation banner instead of the
    /// "update available" one.
    /// </summary>
    public void ReportCompletedUpdate(string installedVersion)
    {
        if (string.IsNullOrWhiteSpace(installedVersion))
        {
            return;
        }

        JustUpdatedToVersion = installedVersion;
        availableUpdate = null;
        updatePresentationState = UpdatePresentationState.None;
        UpdateBannerTitle = localization.Format("Update.Completed.Title", installedVersion);
        UpdateBannerDetail = localization.GetString("Update.Completed.Detail");
        OnPropertyChanged(nameof(JustUpdatedToVersion));
        OnPropertyChanged(nameof(IsUpdateBannerVisible));
        OnPropertyChanged(nameof(CanDownloadUpdate));
        OnPropertyChanged(nameof(IsUpdateActionVisible));
        OnPropertyChanged(nameof(IsUpdateCompletedBannerVisible));
    }

    /// <summary>
    /// Hides the post-update confirmation banner after the user dismisses it.
    /// </summary>
    public void DismissCompletedUpdateBanner()
    {
        if (JustUpdatedToVersion is null)
        {
            return;
        }

        JustUpdatedToVersion = null;
        UpdateBannerTitle = string.Empty;
        UpdateBannerDetail = string.Empty;
        OnPropertyChanged(nameof(JustUpdatedToVersion));
        OnPropertyChanged(nameof(IsUpdateBannerVisible));
        OnPropertyChanged(nameof(IsUpdateActionVisible));
        OnPropertyChanged(nameof(IsUpdateCompletedBannerVisible));
    }

    public void SelectProfile(OptimizationProfile profile)
    {
        if (selectedProfile == profile)
        {
            return;
        }

        profileInitializedFromDiagnostic = true;
        selectedProfile = profile;
        OnPropertyChanged(nameof(IsLightSelected));
        OnPropertyChanged(nameof(IsBalancedSelected));
        OnPropertyChanged(nameof(IsAggressiveSelected));
        OnPropertyChanged(nameof(SelectedProfileLabel));
        OnPropertyChanged(nameof(SelectedProfileName));
        OnPropertyChanged(nameof(IsSelectedProfileRecommended));
        OnPropertyChanged(nameof(ProfileIntensity));
        OnPropertyChanged(nameof(ProfileIntensityPercent));
        RefreshPlan();
    }

    public void SelectTheme(AppThemePreference theme)
    {
        if (!Enum.IsDefined(theme) || themePreference == theme)
        {
            return;
        }

        themePreference = theme;
        OnPropertyChanged(nameof(ThemePreference));
        OnPropertyChanged(nameof(IsSystemThemeSelected));
        OnPropertyChanged(nameof(IsDarkThemeSelected));
        OnPropertyChanged(nameof(IsLightThemeSelected));
        SettingsChanged(refreshPlan: false);
    }

    public void SelectLanguage(AppLanguage language)
    {
        if (!Enum.IsDefined(language))
        {
            return;
        }

        var preference = language switch
        {
            AppLanguage.English => AppLanguagePreference.English,
            AppLanguage.PortugueseBrazil => AppLanguagePreference.PortugueseBrazil,
            AppLanguage.Spanish => AppLanguagePreference.Spanish,
            _ => AppLanguagePreference.English
        };
        if (languagePreference == preference)
        {
            return;
        }

        localization.SetLanguage(language);
        languagePreference = preference;
        RefreshLocalizedState();
        SettingsChanged(refreshPlan: false);
    }

    public async Task StartOptimizationAsync()
    {
        if (!TryPrepareOptimizationRun())
        {
            return;
        }

        operationCancellation = new CancellationTokenSource();
        var progress = new Progress<AppProgressUpdate>(ApplyProgress);
        var completedSuccessfully = false;
        var telemetryEventName = "optimization-failed";
        string? telemetryErrorCategory = null;
        try
        {
            // currentPlan é garantido não-nulo aqui: TryPrepareOptimizationRun
            // só retorna true quando CanStart é true (e CanStart exige plano).
            var result = await service.ExecuteAsync(currentPlan!, progress, operationCancellation.Token);
            completedSuccessfully = result.Succeeded;
            telemetryEventName = result.Succeeded ? "optimization-completed" : "optimization-failed";
            await HandleOptimizationResultAsync(result);
        }
        catch (OperationCanceledException)
        {
            telemetryEventName = "optimization-cancelled";
            telemetryErrorCategory = "cancelled";
            HandleOptimizationCancelled();
        }
        catch (Exception exception)
        {
            telemetryEventName = "optimization-failed";
            telemetryErrorCategory = TelemetryErrorClassifier.ClassifyException(exception);
            HandleOptimizationFailed();
        }
        finally
        {
            FinalizeOptimizationRun(completedSuccessfully, telemetryEventName, telemetryErrorCategory);
        }
    }

    private bool TryPrepareOptimizationRun()
    {
        // Recria o plano no clique para que o nonce e o timestamp aceitos pelo
        // broker elevado nunca fiquem antigos enquanto a janela permanece aberta.
        RefreshPlan();
        if (!CanStart || currentPlan is null)
        {
            ProgressHeadline = diagnostic?.IsFiveMRunning == true
                ? localization.GetString("Plan.CloseFiveM")
                : diagnostic?.GtaVIsRunning == true
                    ? localization.GetString("Plan.CloseGtaV")
                    : localization.GetString("Plan.Unavailable");
            return false;
        }

        IsBusy = true;
        ProgressPercent = 0;
        ClearProgressHistory();
        StartOperationTiming();
        StepLedger.Clear();
        ApplyReport(null);
        ApplyComparison(null);
        lastTransactionId = null;
        return true;
    }

    private async Task HandleOptimizationResultAsync(AppOptimizationResult result)
    {
        ProgressPercent = result.Succeeded ? 100 : ProgressPercent;
        FinalizeHeadline(result.Succeeded
            ? localization.GetString("Status.OptimizationCompleted")
            : result.Summary);
        ApplyReport(result.Report);
        lastTransactionId = result.TransactionId;
        ApplyComparison(result.Comparison);
        ApplyHistory(await service.LoadHistoryAsync());
    }

    private void HandleOptimizationCancelled()
    {
        FinalizeHeadline(localization.GetString("Status.SafeCancellation.Headline"));
    }

    private void HandleOptimizationFailed()
    {
        FinalizeHeadline(localization.GetString("Status.CouldNotComplete"));
    }

    private void FinalizeOptimizationRun(
        bool completedSuccessfully,
        string telemetryEventName,
        string? telemetryErrorCategory)
    {
        var executionTime = operationStopwatch?.Elapsed ?? TimeSpan.Zero;
        StopOperationTiming(completedSuccessfully);
        TrackOptimizationTelemetry(telemetryEventName, executionTime, telemetryErrorCategory);
        // operationCancellation foi atribuído antes do try em StartOptimizationAsync.
        operationCancellation!.Dispose();
        operationCancellation = null;
        IsBusy = false;
    }

    public void CancelOptimization()
    {
        if (operationCancellation is null)
        {
            return;
        }

        operationCancellation.Cancel();
        RaiseCommandState();
    }

    public async Task RunGtaVBenchmarkAsync()
    {
        if (!CanRunGtaVBenchmark)
        {
            return;
        }

        IsGtaVBenchmarkRunning = true;
        GtaVBenchmarkStatusLabel = localization.GetString("GtaVBenchmark.Running");
        RaiseCommandState();
        try
        {
            var result = await service.RunGtaVBenchmarkAsync(3);
            GtaVBenchmarkStatusLabel = DescribeGtaVBenchmarkResult(result);
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            GtaVBenchmarkStatusLabel = localization.Format("GtaVBenchmark.Error", localization.DescribeException(exception));
        }
        finally
        {
            IsGtaVBenchmarkRunning = false;
            RaiseCommandState();
        }
    }

    private string DescribeGtaVBenchmarkResult(AppGtaVBenchmarkResult result)
    {
        if (!result.Succeeded || result.Median is null)
        {
            var reasonKey = result.FailureReason switch
            {
                "gtav-not-detected" => "GtaVBenchmark.Failure.NotDetected",
                "gtav-still-running" => "GtaVBenchmark.Failure.StillRunning",
                "gta-executable-not-found" => "GtaVBenchmark.Failure.NotDetected",
                "profile-folder-not-found" => "GtaVBenchmark.Failure.OutputNotFound",
                "benchmark-output-file-not-found" => "GtaVBenchmark.Failure.OutputNotFound",
                "benchmark-output-file-not-recognized" => "GtaVBenchmark.Failure.OutputNotRecognized",
                "benchmark-did-not-exit-in-time" => "GtaVBenchmark.Failure.Timeout",
                _ => "GtaVBenchmark.Failure.Generic"
            };
            return localization.GetString(reasonKey);
        }

        return localization.Format(
            "GtaVBenchmark.Result",
            result.Median.AverageFps,
            result.Median.MinimumFps,
            result.Median.OnePercentLowFps,
            result.Median.PointOnePercentLowFps,
            result.Iterations.Count);
    }

    public async Task<bool> RevertLastOptimizationAsync()
    {
        if (lastTransactionId is not { } id)
        {
            return false;
        }

        var item = HistoryItems.FirstOrDefault(candidate => candidate.TransactionId == id);
        return item is not null && await RollbackAsync(item);
    }

    public async Task<bool> RollbackAsync(HistoryDisplayItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (IsBusy || !item.CanRollback)
        {
            return false;
        }

        operationCancellation = new CancellationTokenSource();
        IsBusy = true;
        ProgressPercent = 0;
        ClearProgressHistory();
        StartOperationTiming();
        var progress = new Progress<AppProgressUpdate>(ApplyProgress);
        var completedSuccessfully = false;
        try
        {
            var restored = await service.RollbackAsync(item.TransactionId, progress, operationCancellation.Token);
            completedSuccessfully = restored;
            ApplyHistory(await service.LoadHistoryAsync());
            return restored;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception)
        {
            FinalizeHeadline(localization.GetString("Status.CouldNotRestore"));
            return false;
        }
        finally
        {
            StopOperationTiming(completedSuccessfully);
            operationCancellation.Dispose();
            operationCancellation = null;
            IsBusy = false;
        }
    }

    private void ApplyDiagnostic(AppDiagnostic value)
    {
        diagnostic = value;
        OnPropertyChanged(nameof(IsLightRecommended));
        OnPropertyChanged(nameof(IsBalancedRecommended));
        OnPropertyChanged(nameof(IsAggressiveRecommended));
        OnPropertyChanged(nameof(SelectedProfileLabel));
        OnPropertyChanged(nameof(SelectedProfileName));
        OnPropertyChanged(nameof(IsSelectedProfileRecommended));
        if (!profileInitializedFromDiagnostic)
        {
            selectedProfile = value.RecommendedProfile;
            profileInitializedFromDiagnostic = true;
            OnPropertyChanged(nameof(IsLightSelected));
            OnPropertyChanged(nameof(IsBalancedSelected));
            OnPropertyChanged(nameof(IsAggressiveSelected));
            OnPropertyChanged(nameof(SelectedProfileLabel));
            OnPropertyChanged(nameof(SelectedProfileName));
            OnPropertyChanged(nameof(IsSelectedProfileRecommended));
            OnPropertyChanged(nameof(ProfileIntensity));
            OnPropertyChanged(nameof(ProfileIntensityPercent));
        }

        CpuName = value.CpuName;
        GpuDetail = value.GpuNames.Count > 1
            ? string.Join(Environment.NewLine, value.GpuNames)
            : value.GpuName;
        RamLabel = string.IsNullOrWhiteSpace(value.MemoryModuleLayout)
            ? localization.Format("Diagnosis.MemoryTotal", value.TotalMemoryGiB)
            : localization.Format("Diagnosis.MemoryModules", value.TotalMemoryGiB, value.MemoryModuleLayout);
        DiskLabel = localization.Format("Diagnosis.DiskCapacity", value.FreeDiskGiB);
        WindowsLabel = value.OsLabel;
        LogicalProcessorLabel = value.LogicalProcessorCount.ToString(localization.CurrentCulture);
        LogicalProcessorDetail = localization.GetString("Dashboard.Kpi.Cores.Detail");
        AvailableMemoryLabel = localization.Format("Dashboard.Kpi.GigabyteValue", value.AvailableMemoryGiB);
        AvailableMemoryDetail = localization.Format("Dashboard.Kpi.Memory.Detail", value.TotalMemoryGiB);
        (LegacyCacheLabel, LegacyCacheDetail) = DescribeLegacyCache(value.LegacyCacheBytes);
        PerformancePressureLabel = value.PerformancePressure switch
        {
            PerformancePressureLevel.Low => localization.GetString("Dashboard.Pressure.Low"),
            PerformancePressureLevel.High => localization.GetString("Dashboard.Pressure.High"),
            _ => localization.GetString("Dashboard.Pressure.Moderate")
        };
        PerformancePressureBrushKey = value.PerformancePressure switch
        {
            PerformancePressureLevel.Low => "GreenBrush",
            PerformancePressureLevel.High => "RedBrush",
            _ => "YellowBrush"
        };
        LastScanLabel = localization.Format(
            "Dashboard.LastScan",
            DateTime.Now.ToString("HH:mm", localization.CurrentCulture));
        ReadinessScoreExplanation = localization.GetString("Dashboard.ReadinessExplanation");
        ReadinessScore = value.ReadinessScore;
        ReadinessLevelLabel = ReadinessScore switch
        {
            > 75 => localization.GetString("Dashboard.Readiness.Excellent"),
            > 50 => localization.GetString("Dashboard.Readiness.Good"),
            > 25 => localization.GetString("Dashboard.Readiness.Average"),
            > 5 => localization.GetString("Dashboard.Readiness.Poor"),
            _ => localization.GetString("Dashboard.Readiness.VeryPoor")
        };
        IsFiveMLegacyDetected = value.Edition == FiveMEdition.Legacy;
        IsGtaVLegacyDetected = value.GtaVDetected || File.Exists(value.GtaVGraphicsSettingsPath);
        EditionLabel = IsFiveMLegacyDetected
            ? localization.GetString("Diagnosis.FiveMLegacyDetected")
            : localization.GetString("Diagnosis.FiveMNotFound");
        EditionBadgeLabel = value.Edition switch
        {
            FiveMEdition.Legacy => "LEGACY",
            FiveMEdition.Enhanced => "ENHANCED",
            _ => localization.GetString("Status.Waiting")
        };
        GtaStatusLabel = IsGtaVLegacyDetected
            ? localization.GetString("Diagnosis.GtaVLegacyDetected")
            : localization.GetString("Diagnosis.GtaVNotFound");
        RecommendationTitle = value.IsFiveMRunning
            ? localization.GetString("Diagnosis.CloseFiveMSafely")
            : value.GtaVIsRunning
                ? localization.GetString("Diagnosis.CloseGtaVSafely")
            : localization.Format("Diagnosis.RecommendedProfile", ProfileName(value.RecommendedProfile));
        RecommendationText = value.Edition switch
        {
            FiveMEdition.Legacy => localization.GetString("Diagnosis.LegacyReady"),
            FiveMEdition.Enhanced => localization.GetString("Diagnosis.EnhancedUnsupported"),
            _ => localization.GetString("Diagnosis.InstallLegacy")
        };
        ApplyStreamingReadiness(value);
    }

    /// <summary>
    /// Formats the FiveM cache footprint for the overview. Below one gibibyte
    /// the value is shown in mebibytes so a small cache does not collapse into
    /// "0,0 GB"; a missing installation reports no size instead of a zero.
    /// </summary>
    private (string Value, string Detail) DescribeLegacyCache(long bytes)
    {
        if (bytes <= 0)
        {
            return (
                localization.GetString("Dashboard.Kpi.Cache.None"),
                localization.GetString("Dashboard.Kpi.Cache.NoneDetail"));
        }

        const double bytesPerMiB = 1024d * 1024;
        var value = bytes >= 1024L * 1024 * 1024
            ? localization.Format("Dashboard.Kpi.GigabyteValue", bytes / (bytesPerMiB * 1024))
            : localization.Format("Dashboard.Kpi.MegabyteValue", bytes / bytesPerMiB);
        return (value, localization.GetString("Dashboard.Kpi.Cache.Detail"));
    }

    private void ApplyStreamingReadiness(AppDiagnostic value)
    {
        var assessment = StreamingReadinessAdvisor.Evaluate(value);
        (StreamingReadinessTitle, StreamingReadinessDetail) = assessment.Level switch
        {
            StreamingReadinessLevel.Protected => (
                localization.GetString("Streaming.Readiness.Protected.Title"),
                localization.GetString("Streaming.Readiness.Protected.Detail")),
            StreamingReadinessLevel.Attention => (
                localization.GetString("Streaming.Readiness.Attention.Title"),
                localization.GetString("Streaming.Readiness.Attention.Detail")),
            StreamingReadinessLevel.Ready => (
                localization.GetString("Streaming.Readiness.Ready.Title"),
                localization.GetString("Streaming.Readiness.Ready.Detail")),
            StreamingReadinessLevel.Partial => (
                localization.GetString("Streaming.Readiness.Partial.Title"),
                localization.GetString("Streaming.Readiness.Partial.Detail")),
            _ => (
                localization.GetString("Streaming.Readiness.NotDetected.Title"),
                localization.GetString("Streaming.Readiness.NotDetected.Detail"))
        };

        StreamingReadinessItems.Clear();
        foreach (var check in assessment.Checks)
        {
            StreamingReadinessItems.Add(CreateStreamingReadinessItem(check));
        }
    }

    private StreamingReadinessDisplayItem CreateStreamingReadinessItem(StreamingReadinessCheck check)
    {
        var suffix = check.Kind switch
        {
            StreamingReadinessCheckKind.Software => check.Tone switch
            {
                StreamingReadinessTone.Protected => "Protected",
                StreamingReadinessTone.Caution => "Partial",
                StreamingReadinessTone.Ready => "Detected",
                _ => "NotDetected"
            },
            StreamingReadinessCheckKind.Resources => check.Tone switch
            {
                StreamingReadinessTone.Ready => "Ready",
                StreamingReadinessTone.Caution => "Attention",
                _ => "Review"
            },
            StreamingReadinessCheckKind.GameSession => check.Tone == StreamingReadinessTone.Caution
                ? "Open"
                : "Closed",
            _ => throw new ArgumentOutOfRangeException(nameof(check))
        };
        var icon = check.Kind switch
        {
            StreamingReadinessCheckKind.Software => "IconStream",
            StreamingReadinessCheckKind.Resources => "IconPulse",
            StreamingReadinessCheckKind.GameSession => "IconGame",
            _ => "IconInfo"
        };
        var tone = check.Tone switch
        {
            StreamingReadinessTone.Protected => "GreenBrush",
            StreamingReadinessTone.Ready => "GreenBrush",
            StreamingReadinessTone.Caution => "YellowBrush",
            _ => "TextSubtleBrush"
        };
        var title = localization.GetString($"Streaming.Check.{check.Kind}.{suffix}.Title");
        var detail = check.Kind == StreamingReadinessCheckKind.Software
            && check.ApplicationNames.Count > 0
            ? localization.Format(
                $"Streaming.Check.{check.Kind}.{suffix}.DetailWithNames",
                string.Join(", ", check.ApplicationNames))
            : localization.GetString($"Streaming.Check.{check.Kind}.{suffix}.Detail");

        return new StreamingReadinessDisplayItem(icon, title, detail, tone);
    }

    private void ApplySettings(AppSettings settings)
    {
        languagePreference = Enum.IsDefined(settings.Language)
            ? settings.Language
            : AppLanguagePreference.Automatic;
        localization.Apply(languagePreference);
        themePreference = Enum.IsDefined(settings.Theme)
            ? settings.Theme
            : AppThemePreference.System;
        minimizeToTrayOnClose = settings.MinimizeToTrayOnClose;
        checkForUpdates = settings.CheckForUpdates;
        shareAnonymousTelemetry = settings.ShareAnonymousTelemetry;
        telemetry.SetEnabled(shareAnonymousTelemetry);
        shareCrashReports = settings.ShareCrashReports;
        privacyConsentVersion = settings.PrivacyConsentVersion;
        try
        {
            launchAtStartup = startupRegistration.IsEnabled();
        }
        catch (Exception)
        {
            launchAtStartup = settings.LaunchAtStartup;
        }

        OnPropertyChanged(nameof(LanguagePreference));
        OnPropertyChanged(nameof(CurrentLanguage));
        OnPropertyChanged(nameof(IsEnglishSelected));
        OnPropertyChanged(nameof(IsPortugueseSelected));
        OnPropertyChanged(nameof(IsSpanishSelected));
        OnPropertyChanged(nameof(ThemePreference));
        OnPropertyChanged(nameof(IsSystemThemeSelected));
        OnPropertyChanged(nameof(IsDarkThemeSelected));
        OnPropertyChanged(nameof(IsLightThemeSelected));
        OnPropertyChanged(nameof(MinimizeToTrayOnClose));
        OnPropertyChanged(nameof(IsCloseAppOnCloseSelected));
        OnPropertyChanged(nameof(IsMinimizeToTrayOnCloseSelected));
        OnPropertyChanged(nameof(LaunchAtStartup));
        OnPropertyChanged(nameof(CheckForUpdates));
        OnPropertyChanged(nameof(ShareAnonymousTelemetry));
        OnPropertyChanged(nameof(ShareCrashReports));
        ResetLocalizedPlaceholders(preserveDiagnostic: true);
    }

    private void ApplyHistory(IReadOnlyList<AppHistoryRecord> records)
    {
        historyRecords = records;
        HistoryItems.Clear();
        foreach (var record in records.OrderByDescending(item => item.CreatedAt).Take(30))
        {
            HistoryItems.Add(new HistoryDisplayItem(
                record.TransactionId,
                localization.Format("History.ProfileTitle", ProfileName(record.Profile)),
                record.CreatedAt.LocalDateTime.ToString("g", localization.CurrentCulture),
                localization.Format("History.AdjustmentsState", record.ChangedActions, record.State),
                record.CanRollback));
        }

        // A composição vazia (silhueta do núcleo + texto) vive na própria
        // página; a coleção precisa continuar realmente vazia para que ela
        // apareça, em vez de uma linha de ledger fantasma com "Desfazer"
        // desabilitado — reverter uma execução que não existe não faz sentido.
        ApplyLastOptimization(records);
        OnPropertyChanged(nameof(CanRevertLastOptimization));
    }

    /// <summary>
    /// Summarizes the most recent run for the overview. With no history at all
    /// the card explains that state instead of disappearing and leaving a gap
    /// in the page.
    /// </summary>
    private void ApplyLastOptimization(IReadOnlyList<AppHistoryRecord> records)
    {
        var latest = records.Count == 0
            ? null
            : records.OrderByDescending(item => item.CreatedAt).First();

        HasLastOptimization = latest is not null;
        if (latest is null)
        {
            LastOptimizationTitle = localization.GetString("Dashboard.LastRun.None.Title");
            LastOptimizationDateLabel = string.Empty;
            LastOptimizationSummary = localization.GetString("Dashboard.LastRun.None.Detail");
            return;
        }

        LastOptimizationTitle = localization.Format("History.ProfileTitle", ProfileName(latest.Profile));
        LastOptimizationDateLabel = latest.CreatedAt.LocalDateTime.ToString("g", localization.CurrentCulture);
        LastOptimizationSummary = localization.Format(
            "History.AdjustmentsState",
            latest.ChangedActions,
            latest.State);
    }

    /// <summary>
    /// Called by the window whenever the signed-in account's own profile is
    /// (re)read from the Worker — on login and on quiet session restore —
    /// and with <see langword="null"/> on sign-out. Firebase Authentication
    /// REST never stores a first name, so this is the only path that can
    /// ever populate it.
    /// </summary>
    public void SetAccountFirstName(string? firstName)
    {
        accountFirstName = string.IsNullOrWhiteSpace(firstName) ? null : firstName;
        RefreshGreeting();
    }

    /// <summary>
    /// Recomputes <see cref="GreetingTitle"/> from the machine's local clock.
    /// Boundaries: 06:00–11:59 morning, 12:00–17:59 afternoon, otherwise
    /// evening/night (18:00–05:59) — a plain three-way split a player reads
    /// the same way they would read a clock, not a technical period name.
    /// </summary>
    private void RefreshGreeting()
    {
        var hour = DateTime.Now.Hour;
        var period = hour switch
        {
            >= 6 and < 12 => "Morning",
            >= 12 and < 18 => "Afternoon",
            _ => "Evening"
        };
        GreetingTitle = accountFirstName is { } name
            ? localization.Format($"Greeting.{period}.WithName", name)
            : localization.GetString($"Greeting.{period}.NoName");
    }

    private void RefreshPlan()
    {
        var edition = diagnostic?.Edition ?? FiveMEdition.Unknown;
        var options = new OptimizationOptionsDto
        {
            CleanUserTemporaryFiles = true,
            TemporaryFileMinimumAgeDays = selectedProfile switch
            {
                OptimizationProfile.Light => 30,
                OptimizationProfile.Balanced => 14,
                _ => 7
            },
            RemoveOldFiveMCrashDumps = true,
            DiagnosticRetentionDays = selectedProfile == OptimizationProfile.Aggressive ? 7 : 14,
            ServerCacheRepair = selectedProfile == OptimizationProfile.Light
                ? CacheRepairPolicy.Off
                : CacheRepairPolicy.WhenOversized,
            ServerCacheThresholdGiB = 8,
            EnableGameMode = true,
            PreferHighPerformanceGpu = true,
            DisableBackgroundCapture = true,
            UseSessionPerformancePowerPlan = selectedProfile != OptimizationProfile.Light,
            ApplyLegacyGraphicsPreset = true,
            ApplyGtaVGraphicsPreset = diagnostic?.GtaVDetected == true,
            ReduceWindowsVisualEffects = selectedProfile == OptimizationProfile.Aggressive
        };

        currentPlan = PlanBuilder.Build(
            new OptimizationPlanRequestDto
            {
                Profile = selectedProfile,
                Edition = edition,
                Options = options
            },
            PlanBuildContext.New(TimeProvider.System));

        PlannedActions.Clear();
        foreach (var action in currentPlan.Actions)
        {
            PlannedActions.Add(ToDisplayItem(action.Metadata));
        }

        OnPropertyChanged(nameof(SelectedActionCount));
        OnPropertyChanged(nameof(ElevationLabel));
        OnPropertyChanged(nameof(PlanSummary));
        OnPropertyChanged(nameof(PlanHeader));
        OnPropertyChanged(nameof(PlanNoticesText));
        OnPropertyChanged(nameof(SafetySummary));
        OnPropertyChanged(nameof(AboutVersionDeveloper));
        RefreshProfilePresentation();
        RaiseCommandState();
    }

    private void RefreshProfilePresentation()
    {
        var presentation = ProfilePresentationProvider.For(selectedProfile);
        ProfilePresentationBenefits = localization.GetString($"Profiles.Presentation.{selectedProfile}.Benefits");
        ProfilePresentationImpact = localization.GetString($"Profiles.Presentation.Impact.{presentation.ImpactLevel}");
        ProfilePresentationCategories = string.Join(
            "  •  ",
            presentation.AnalyzedCategories.Select(category =>
                localization.GetString($"Category.{category}")));
    }

    private AppSettings BuildSettingsSnapshot() => new()
    {
        Language = languagePreference,
        Theme = ThemePreference,
        MinimizeToTrayOnClose = MinimizeToTrayOnClose,
        LaunchAtStartup = LaunchAtStartup,
        CheckForUpdates = CheckForUpdates,
        ShareAnonymousTelemetry = ShareAnonymousTelemetry,
        ShareCrashReports = ShareCrashReports,
        PrivacyConsentVersion = privacyConsentVersion
    };

    private void SettingsChanged(bool refreshPlan = true)
    {
        if (refreshPlan)
        {
            RefreshPlan();
        }

        var revision = Interlocked.Increment(ref settingsRevision);
        _ = SaveSettingsRevisionAsync(BuildSettingsSnapshot(), revision);
    }

    /// <summary>
    /// Persists the outcome of the privacy consent screen: whether the user
    /// clicked "Continue" with their chosen toggles, or closed the window
    /// (interpreted by the caller as declining both — pass
    /// <see langword="false"/>/<see langword="false"/>). Always stamps
    /// <see cref="PrivacyConsentPolicy.CurrentVersion"/> so the screen does
    /// not reappear next launch, and always reuses the same settings
    /// persistence path as every other preference
    /// (<see cref="IAppOptimizationService.SaveSettingsAsync"/>) — no second
    /// storage mechanism is introduced.
    /// </summary>
    public async Task ConfirmPrivacyConsentAsync(bool acceptAnonymousTelemetry, bool acceptCrashReports = true)
    {
        var snapshot = PrivacyConsentOutcomeBuilder.BuildConfirmed(
            BuildSettingsSnapshot(),
            acceptAnonymousTelemetry,
            acceptCrashReports);

        shareAnonymousTelemetry = snapshot.ShareAnonymousTelemetry;
        telemetry.SetEnabled(snapshot.ShareAnonymousTelemetry);
        shareCrashReports = snapshot.ShareCrashReports;
        privacyConsentVersion = snapshot.PrivacyConsentVersion;
        OnPropertyChanged(nameof(ShareAnonymousTelemetry));
        OnPropertyChanged(nameof(ShareCrashReports));
        PrivacyConsentDecision = null;

        var revision = Interlocked.Increment(ref settingsRevision);
        await SaveSettingsRevisionAsync(snapshot, revision).ConfigureAwait(false);
    }

    private void TrackOptimizationTelemetry(
        string eventName,
        TimeSpan executionTime,
        string? errorCategory)
    {
        if (!telemetry.IsEnabled)
        {
            return;
        }

        var telemetryEvent = new AnonymousTelemetryEvent(
            eventName,
            executionTime,
            AppVersion.TrimStart('v', 'V'),
            errorCategory,
            OsVersion: diagnostic?.OsLabel,
            SystemArchitecture: diagnostic?.SystemArchitecture,
            CpuModel: ShareAnonymousTelemetry ? diagnostic?.CpuName : null,
            GpuModel: ShareAnonymousTelemetry ? diagnostic?.GpuName : null,
            RamBucketGiB: ShareAnonymousTelemetry && diagnostic is not null ? RamBucketCalculator.ComputeBucketGiB(diagnostic.TotalMemoryGiB) : null,
            Profile: ShareAnonymousTelemetry ? selectedProfile.ToString() : null,
            ActionIds: ShareAnonymousTelemetry ? currentPlan?.Actions
                .Select(action => action.Metadata.Id)
                .Take(TelemetryEventValidator.MaxActionIds)
                .ToArray() : null);
        _ = TrackOptimizationTelemetryAsync(telemetryEvent);
    }

    private async Task TrackOptimizationTelemetryAsync(AnonymousTelemetryEvent telemetryEvent)
    {
        try
        {
            await telemetry.TrackAsync(telemetryEvent).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            // Telemetria é opcional e não pode afetar a experiência nem gerar logs locais adicionais.
        }
    }

    private async Task SaveSettingsRevisionAsync(AppSettings snapshot, long revision)
    {
        try
        {
            await settingsSaveGate.WaitAsync();
            try
            {
                if (revision != Volatile.Read(ref settingsRevision))
                {
                    return;
                }

                await service.SaveSettingsAsync(snapshot);
            }
            finally
            {
                settingsSaveGate.Release();
            }
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            // Settings are best-effort; a later revision can still persist.
        }
    }

    private void ApplyProgress(AppProgressUpdate update)
    {
        ProgressPercent = Math.Clamp(update.Percent, 0, 100);
        EnqueueHeadline(update.Headline);
        if (update.ActionId is not null && update.Outcome is { } outcome
            && outcome != ActionExecutionOutcome.Pending)
        {
            UpsertStepLedgerItem(update.ActionId, outcome);
        }

        UpdateOperationTiming();
    }

    private void ClearProgressHistory()
    {
        headlineDwellTimer?.Stop();
        headlineDwellTimer = null;
        pendingHeadlines.Clear();
        headlineShownAtUtc = default;
        previousProgressHeadline = string.Empty;
        progressHeadline = string.Empty;
        OnPropertyChanged(nameof(PreviousProgressHeadline));
        OnPropertyChanged(nameof(HasPreviousProgressHeadline));
        OnPropertyChanged(nameof(ProgressHeadline));
    }

    /// <summary>
    /// Cada passo do otimizador fica visível pelo menos <see cref="HeadlineMinimumDwell"/>
    /// antes de dar lugar ao próximo, em qualquer modo (leve/médio/agressivo).
    /// </summary>
    private void EnqueueHeadline(string headline)
    {
        if (string.IsNullOrWhiteSpace(headline)
            || string.Equals(headline, ProgressHeadline, StringComparison.Ordinal)
            || (pendingHeadlines.Count > 0 && string.Equals(pendingHeadlines.Last(), headline, StringComparison.Ordinal)))
        {
            return;
        }

        pendingHeadlines.Enqueue(headline);
        AdvanceHeadlineQueue();
    }

    private void AdvanceHeadlineQueue()
    {
        if (headlineDwellTimer is not null || pendingHeadlines.Count == 0)
        {
            return;
        }

        var elapsed = DateTime.UtcNow - headlineShownAtUtc;
        if (elapsed >= HeadlineMinimumDwell)
        {
            ShowNextQueuedHeadline();
        }
        else
        {
            StartHeadlineDwellTimer(HeadlineMinimumDwell - elapsed);
        }
    }

    private void ShowNextQueuedHeadline()
    {
        ProgressHeadline = pendingHeadlines.Dequeue();
        headlineShownAtUtc = DateTime.UtcNow;
        StartHeadlineDwellTimer(HeadlineMinimumDwell);
    }

    private void StartHeadlineDwellTimer(TimeSpan due)
    {
        headlineDwellTimer?.Stop();
        headlineDwellTimer = new DispatcherTimer
        {
            Interval = due > TimeSpan.Zero ? due : TimeSpan.FromMilliseconds(1)
        };
        headlineDwellTimer.Tick += OnHeadlineDwellTimerTick;
        headlineDwellTimer.Start();
    }

    private void OnHeadlineDwellTimerTick(object? sender, EventArgs e)
    {
        headlineDwellTimer?.Stop();
        headlineDwellTimer = null;

        if (pendingHeadlines.Count > 0)
        {
            ShowNextQueuedHeadline();
        }
    }

    /// <summary>
    /// Usado para estados terminais (concluído, cancelado, falhou): descarta a fila de
    /// passos pendentes e mostra o resultado final imediatamente, sem esperar o dwell.
    /// </summary>
    private void FinalizeHeadline(string headline)
    {
        headlineDwellTimer?.Stop();
        headlineDwellTimer = null;
        pendingHeadlines.Clear();
        ProgressHeadline = headline;
        headlineShownAtUtc = DateTime.UtcNow;
    }

    private void UpsertStepLedgerItem(string actionId, ActionExecutionOutcome outcome)
    {
        var name = GetLocalizedActionName(actionId, actionId);
        var (label, glyph, brushKey) = DescribeOutcome(outcome);
        var item = new StepLedgerItem(actionId, name, outcome, label, glyph, brushKey);
        var existingIndex = -1;
        for (var index = 0; index < StepLedger.Count; index++)
        {
            if (StepLedger[index].ActionId == actionId)
            {
                existingIndex = index;
                break;
            }
        }

        if (existingIndex >= 0)
        {
            StepLedger[existingIndex] = item;
        }
        else
        {
            StepLedger.Add(item);
        }
    }

    private string GetLocalizedActionName(string actionId, string fallback)
    {
        var key = $"Actions.{actionId}.Name";
        var value = localization.GetString(key);
        return value == key ? fallback : value;
    }

    private (string Label, string Glyph, string BrushKey) DescribeOutcome(ActionExecutionOutcome outcome)
    {
        return outcome switch
        {
            ActionExecutionOutcome.Verified => (localization.GetString("Outcome.Verified"), "IconMarkVerified", "InfoBaseBrush"),
            ActionExecutionOutcome.Applied => (localization.GetString("Outcome.Applied"), "IconMarkApplied", "SuccessBaseBrush"),
            ActionExecutionOutcome.Skipped => (localization.GetString("Outcome.Skipped"), "IconMarkSkipped", "NeutralBaseBrush"),
            ActionExecutionOutcome.Warning => (localization.GetString("Outcome.Warning"), "IconMarkWarning", "WarningBaseBrush"),
            ActionExecutionOutcome.Failed => (localization.GetString("Outcome.Failed"), "IconMarkFailed", "DangerBaseBrush"),
            ActionExecutionOutcome.RolledBack => (localization.GetString("Outcome.RolledBack"), "IconMarkRolledBack", "RevertBaseBrush"),
            ActionExecutionOutcome.RollbackFailed => (localization.GetString("Outcome.RollbackFailed"), "IconMarkRollbackFailed", "DangerBaseBrush"),
            ActionExecutionOutcome.NotRun => (localization.GetString("Outcome.NotRun"), "IconMarkNotRun", "TextTertiaryBrush"),
            _ => (localization.GetString("Outcome.Running"), "IconMarkPending", "AccentBrush")
        };
    }

    /// <summary>
    /// Desfecho apresentável do relatório, derivado só de contagens já
    /// existentes em <see cref="OptimizationReportDto"/> — nunca um estado
    /// inventado. A revisão de design pediu quatro tratamentos visuais
    /// distintos (sucesso, sucesso com falhas isoladas, falha, rollback sem
    /// sucesso) e este é o único ponto de decisão para os quatro.
    /// </summary>
    public bool ReportSucceeded => lastReport?.Succeeded ?? false;

    public bool ReportHasRollbackFailures => (lastReport?.RollbackFailedCount ?? 0) > 0;

    public bool ReportHasIsolatedFailures => !ReportSucceeded
        && !ReportHasRollbackFailures
        && (lastReport?.ChangedCount ?? 0) > 0;

    public bool ReportFailedOutright => !ReportSucceeded
        && !ReportHasRollbackFailures
        && (lastReport?.ChangedCount ?? 0) == 0;

    private void ApplyReport(OptimizationReportDto? report)
    {
        lastReport = report;
        IsReportAvailable = report is not null;
        OnPropertyChanged(nameof(CanShareReport));
        OnPropertyChanged(nameof(SuggestedReportFileName));
        OnPropertyChanged(nameof(ReportSucceeded));
        OnPropertyChanged(nameof(ReportHasRollbackFailures));
        OnPropertyChanged(nameof(ReportHasIsolatedFailures));
        OnPropertyChanged(nameof(ReportFailedOutright));
        ReportLines.Clear();
        if (report is null)
        {
            ReportSummaryLabel = string.Empty;
            ReportRestartLabel = string.Empty;
            return;
        }

        ReportSummaryLabel = localization.Format(
            "Report.SummaryFormat",
            report.VerifiedCount,
            report.ChangedCount,
            report.SkippedCount,
            report.WarningCount,
            report.FailedCount);
        ReportRestartLabel = localization.GetString(
            report.RequiresRestart ? "Report.RestartNeeded" : "Report.RestartNotNeeded");

        foreach (var line in report.Lines)
        {
            var (label, glyph, brushKey) = DescribeOutcome(line.Outcome);
            ReportLines.Add(new ReportLineDisplayItem(
                GetLocalizedActionName(line.ActionId, line.ActionName),
                label,
                glyph,
                brushKey,
                line.Reason));
        }
    }

    private void ApplyComparison(OptimizationComparisonResult? comparison)
    {
        lastComparison = comparison;
        IsComparisonAvailable = comparison is not null;
        if (comparison is null)
        {
            ComparisonRegressionSuspected = false;
            ComparisonSummaryLabel = string.Empty;
            ComparisonHardwareProfileLabel = string.Empty;
            OnPropertyChanged(nameof(CanRevertLastOptimization));
            return;
        }

        ComparisonRegressionSuspected = comparison.RegressionSuspected;
        ComparisonSummaryLabel = comparison.RegressionSuspected
            ? localization.GetString("Comparison.RegressionSuspected") + " "
                + string.Join(" ", comparison.RegressionReasons)
            : localization.GetString("Comparison.NoRegression");
        ComparisonHardwareProfileLabel = localization.GetString("Comparison.HardwareProfile")
            + ": " + comparison.HardwareProfileSignature;
        OnPropertyChanged(nameof(CanRevertLastOptimization));
    }

    public bool CanShareReport => lastReport is not null;

    public string SuggestedReportFileName => lastReport is null
        ? "FiveMCleaner-Report.txt"
        : $"FiveMCleaner-Report-{lastReport.TransactionId:N}.txt";

    public void CopyTechnicalReport()
    {
        if (lastReport is null)
        {
            return;
        }

        var text = TechnicalReportBuilder.Build(lastReport, diagnostic, localization);
        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            // Clipboard ownership is best-effort and must not destabilize the UI.
        }
    }

    /// <summary>
    /// Writes the sanitized technical report to a path the user picked
    /// explicitly (via a native save dialog in the code-behind). Never
    /// chooses or guesses a location itself.
    /// </summary>
    public void SaveTechnicalReport(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (lastReport is null)
        {
            return;
        }

        var text = TechnicalReportBuilder.Build(lastReport, diagnostic, localization);
        try
        {
            File.WriteAllText(filePath, text);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            // The caller owns the selected path; a failed export leaves no partial app state.
        }
    }

    private void StartOperationTiming()
    {
        operationTimer?.Stop();
        operationStopwatch = Stopwatch.StartNew();
        progressTimingEstimator.Reset();
        UpdateOperationTiming();

        operationTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        operationTimer.Tick += OperationTimerOnTick;
        operationTimer.Start();
    }

    private void OperationTimerOnTick(object? sender, EventArgs eventArgs)
    {
        UpdateOperationTiming();
    }

    private void StopOperationTiming(bool completedSuccessfully)
    {
        if (operationTimer is not null)
        {
            operationTimer.Stop();
            operationTimer.Tick -= OperationTimerOnTick;
            operationTimer = null;
        }

        if (operationStopwatch is null)
        {
            return;
        }

        operationStopwatch.Stop();
        var elapsed = operationStopwatch.Elapsed;
        ElapsedTimeLabel = localization.Format(
            "Progress.ElapsedFormat",
            FormatDuration(elapsed));
        RemainingTimeLabel = completedSuccessfully
            ? localization.Format("Progress.CompletedInFormat", FormatDuration(elapsed))
            : string.Empty;
        operationStopwatch = null;
        progressTimingEstimator.Reset();
    }

    private void UpdateOperationTiming()
    {
        if (operationStopwatch is null)
        {
            return;
        }

        var elapsed = operationStopwatch.Elapsed;
        ElapsedTimeLabel = localization.Format(
            "Progress.ElapsedFormat",
            FormatDuration(elapsed));

        if (ProgressPercent >= 99)
        {
            RemainingTimeLabel = localization.GetString("Progress.Finishing");
            return;
        }

        if (elapsed < TimeSpan.FromSeconds(2) || ProgressPercent < 3)
        {
            RemainingTimeLabel = localization.GetString("Progress.Calculating");
            return;
        }

        var estimate = progressTimingEstimator.EstimateRemaining(elapsed, ProgressPercent);
        if (estimate is null)
        {
            RemainingTimeLabel = localization.GetString("Progress.Calculating");
            return;
        }

        RemainingTimeLabel = localization.Format(
            "Progress.RemainingFormat",
            FormatDuration(estimate.Value));
    }

    private void ResetLocalizedPlaceholders(bool preserveDiagnostic = false)
    {
        if (!IsBusy)
        {
            ProgressHeadline = localization.GetString("Status.Ready.Headline");
            ElapsedTimeLabel = localization.Format("Progress.ElapsedFormat", "00:00");
            RemainingTimeLabel = localization.GetString("Progress.Calculating");
        }

        if (!preserveDiagnostic || diagnostic is null)
        {
            var analyzing = localization.GetString("Status.Analyzing");
            CpuName = analyzing;
            GpuDetail = analyzing;
            RamLabel = analyzing;
            DiskLabel = analyzing;
            WindowsLabel = analyzing;
            ReadinessScoreExplanation = localization.GetString("Dashboard.ReadinessExplanation");
            ReadinessLevelLabel = analyzing;
            EditionLabel = localization.GetString("Status.SearchingFiveM");
            GtaStatusLabel = localization.GetString("Status.SearchingGtaV");
            IsFiveMLegacyDetected = false;
            IsGtaVLegacyDetected = false;
            RecommendationTitle = localization.GetString("Status.AnalyzingComputer");
            RecommendationText = localization.GetString("Status.LocalOnly");
            LogicalProcessorLabel = analyzing;
            LogicalProcessorDetail = localization.GetString("Dashboard.Kpi.Cores.Detail");
            AvailableMemoryLabel = analyzing;
            AvailableMemoryDetail = string.Empty;
            LegacyCacheLabel = analyzing;
            LegacyCacheDetail = localization.GetString("Dashboard.Kpi.Cache.Detail");
            PerformancePressureLabel = analyzing;
            PerformancePressureBrushKey = "TextMutedBrush";
            LastScanLabel = localization.GetString("Dashboard.LastScan.Pending");
        }

        if (lastLiveMetrics is null)
        {
            CpuUsageLabel = localization.GetString("Dashboard.LivePerformance.Waiting");
            GpuUsageLabel = localization.GetString("Dashboard.LivePerformance.Waiting");
            MemoryUsageLabel = localization.GetString("Dashboard.LivePerformance.Waiting");
            DiskUsageLabel = localization.GetString("Dashboard.LivePerformance.Waiting");
            NetworkUsageLabel = localization.GetString("Dashboard.LivePerformance.Waiting");
            LiveMetricsUpdatedLabel = localization.GetString("Dashboard.LivePerformance.Waiting");
            MemoryUsageDetailLabel = string.Empty;
            CpuTrendLabel = localization.GetString("Dashboard.LivePerformance.NotAvailable");
            GpuTrendLabel = localization.GetString("Dashboard.LivePerformance.NotAvailable");
        }
        else
        {
            ApplyLiveMetrics(lastLiveMetrics, addHistory: false);
        }

        NotifyLivePerformanceStateChanged();
        ApplyLastOptimization(historyRecords);
    }

    private void RefreshLocalizedState()
    {
        RefreshGreeting();
        OnPropertyChanged(nameof(LanguagePreference));
        OnPropertyChanged(nameof(CurrentLanguage));
        OnPropertyChanged(nameof(IsEnglishSelected));
        OnPropertyChanged(nameof(IsPortugueseSelected));
        OnPropertyChanged(nameof(IsSpanishSelected));
        OnPropertyChanged(nameof(SelectedProfileLabel));
        OnPropertyChanged(nameof(SelectedProfileName));
        OnPropertyChanged(nameof(IsSelectedProfileRecommended));
        OnPropertyChanged(nameof(ElevationLabel));
        OnPropertyChanged(nameof(PlanSummary));
        OnPropertyChanged(nameof(PlanHeader));
        OnPropertyChanged(nameof(PlanNoticesText));
        OnPropertyChanged(nameof(SafetySummary));

        ResetLocalizedPlaceholders(preserveDiagnostic: diagnostic is not null);
        if (diagnostic is not null)
        {
            ApplyDiagnostic(diagnostic);
        }

        ApplyHistory(historyRecords);
        RefreshPlan();
        UpdateOperationTiming();
        RefreshUpdatePresentation();
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
        (liveSystemMetricsProvider as IDisposable)?.Dispose();
    }

    private void RefreshUpdatePresentation()
    {
        switch (updatePresentationState)
        {
            case UpdatePresentationState.Available when availableUpdate is not null:
                UpdateBannerTitle = localization.Format(
                    "Update.Available.Title",
                    availableUpdate.Version.CoreVersion);
                UpdateBannerDetail = localization.Format(
                    "Update.Available.Detail",
                    FormatBytes(availableUpdate.SizeBytes));
                break;
            case UpdatePresentationState.Downloading:
                UpdateBannerTitle = localization.GetString("Update.Downloading.Title");
                UpdateBannerDetail = localization.Format(
                    "Update.Downloading.Detail",
                    UpdateDownloadPercent);
                break;
            case UpdatePresentationState.Ready when availableUpdate is not null:
                UpdateBannerTitle = localization.Format(
                    "Update.Ready.Title",
                    availableUpdate.Version.CoreVersion);
                UpdateBannerDetail = localization.GetString("Update.Ready.Detail");
                break;
            case UpdatePresentationState.Installing when availableUpdate is not null:
                UpdateBannerTitle = localization.Format(
                    "Update.Installing.Title",
                    availableUpdate.Version.CoreVersion);
                UpdateBannerDetail = localization.GetString("Update.Installing.Detail");
                break;
            case UpdatePresentationState.Failed:
                UpdateBannerTitle = localization.GetString("Update.Failed.Title");
                UpdateBannerDetail = localization.Format(
                    "Update.Failed.Detail",
                    updateFailureMessage ?? localization.GetString("Common.Unknown"));
                break;
        }

        OnPropertyChanged(nameof(IsUpdateBannerVisible));
        OnPropertyChanged(nameof(IsUpdateActionVisible));
        OnPropertyChanged(nameof(UpdateActionLabel));
        OnPropertyChanged(nameof(UpdateReleaseNotesLabel));
        OnPropertyChanged(nameof(CanDownloadUpdate));
        OnPropertyChanged(nameof(ReleaseNotesUri));
        OnPropertyChanged(nameof(CanOpenReleaseNotes));
    }

    private ActionDisplayItem ToDisplayItem(ActionMetadataDto action)
    {
        var icon = action.Category switch
        {
            ActionCategory.Safety => "\uEA18",
            ActionCategory.Storage => "\uE958",
            ActionCategory.WindowsGaming => "\uE7FC",
            ActionCategory.Power => "\uE945",
            ActionCategory.Appearance => "\uE790",
            ActionCategory.FiveMGraphics => "\uE7F8",
            _ => "\uE946"
        };
        var risk = action.Risk switch
        {
            ActionRisk.Informational => localization.GetString("Risk.Informational"),
            ActionRisk.Low => localization.GetString("Risk.Low"),
            ActionRisk.Moderate => localization.GetString("Risk.Moderate"),
            ActionRisk.High => localization.GetString("Risk.HighReversible"),
            _ => action.Risk.ToString().ToUpperInvariant()
        };
        var riskBrushKey = action.Risk switch
        {
            ActionRisk.Informational => "TextTertiaryBrush",
            ActionRisk.Low => "InfoBaseBrush",
            ActionRisk.Moderate => "WarningBaseBrush",
            ActionRisk.High => "DangerBaseBrush",
            _ => "TextTertiaryBrush"
        };
        var requiresElevation = action.RequiredPrivilege == RequiredPrivilege.Administrator;
        var privilege = requiresElevation
            ? localization.GetString("Privilege.RequiresUac")
            : action.Reversibility is ActionReversibility.Irreversible or ActionReversibility.RebuildableData
                ? localization.GetString("Privilege.PermanentCleanup")
                : localization.GetString("Privilege.Reversible");
        var categoryLabel = action.Category switch
        {
            ActionCategory.Safety => localization.GetString("Category.Safety"),
            ActionCategory.Storage => localization.GetString("Category.Storage"),
            ActionCategory.WindowsGaming => localization.GetString("Category.WindowsGaming"),
            ActionCategory.Power => localization.GetString("Category.Power"),
            ActionCategory.Appearance => localization.GetString("Category.Appearance"),
            ActionCategory.FiveMGraphics => localization.GetString("Category.FiveMGraphics"),
            _ => action.Category.ToString()
        };
        var nameKey = $"Actions.{action.Id}.Name";
        var descriptionKey = $"Actions.{action.Id}.Description";
        var localizedName = localization.GetString(nameKey);
        var localizedDescription = localization.GetString(descriptionKey);
        return new ActionDisplayItem(
            action.Id,
            localizedName == nameKey ? action.Name : localizedName,
            localizedDescription == descriptionKey ? action.Description : localizedDescription,
            icon,
            risk,
            riskBrushKey,
            privilege,
            requiresElevation,
            categoryLabel);
    }

    private string LocalizeNotice(PlanNoticeDto notice) => notice.Code switch
    {
        "diagnostics-removal-is-permanent" => localization.Format(
            "Plan.Notice.DiagnosticsRetention",
            currentPlan?.Options.DiagnosticRetentionDays ?? 14),
        "server-cache-will-be-rebuilt" => localization.GetString("Plan.Notice.ServerCacheRepair"),
        "performance-power-requires-ac" => localization.GetString("Plan.Notice.AcPower"),
        "aggressive-prioritizes-performance" => localization.GetString("Plan.Notice.AggressiveVisual"),
        _ => notice.Message
    };

    private string ProfileName(OptimizationProfile profile) => profile switch
    {
        OptimizationProfile.Light => localization.GetString("Profiles.Light.Name"),
        OptimizationProfile.Balanced => localization.GetString("Profiles.Balanced.Name"),
        OptimizationProfile.Aggressive => localization.GetString("Profiles.Aggressive.Name"),
        _ => profile.ToString()
    };

    private string FormatBytes(long bytes)
    {
        const double giB = 1024d * 1024d * 1024d;
        const double miB = 1024d * 1024d;
        var culture = localization.CurrentCulture;
        return bytes >= giB
            ? $"{(bytes / giB).ToString("0.##", culture)} GB"
            : $"{(bytes / miB).ToString("0.#", culture)} MB";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var rounded = TimeSpan.FromSeconds(Math.Max(0, Math.Round(duration.TotalSeconds)));
        return rounded.TotalHours >= 1
            ? $"{(int)rounded.TotalHours:00}:{rounded.Minutes:00}:{rounded.Seconds:00}"
            : $"{rounded.Minutes:00}:{rounded.Seconds:00}";
    }

    private enum UpdatePresentationState
    {
        None,
        Available,
        Downloading,
        Ready,
        Installing,
        Failed
    }
}
