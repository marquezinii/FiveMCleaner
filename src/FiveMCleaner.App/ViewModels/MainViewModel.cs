using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using FiveMCleaner.App.Services;
using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Planning;

namespace FiveMCleaner.App.ViewModels;

public sealed class MainViewModel : BindableBase
{
    private readonly IAppOptimizationService service;
    private readonly IPlanBuilder planBuilder;
    private readonly ILocalizationService localization;
    private readonly IStartupRegistrationService startupRegistration;
    private readonly IReleaseUpdateService? releaseUpdateService;
    private readonly ProgressTimingEstimator progressTimingEstimator = new();
    private readonly SemaphoreSlim settingsSaveGate = new(1, 1);
    private CancellationTokenSource? operationCancellation;
    private AppDiagnostic? diagnostic;
    private IReadOnlyList<AppHistoryRecord> historyRecords = [];
    private OptimizationPlanDto? currentPlan;
    private OptimizationProfile selectedProfile = OptimizationProfile.Balanced;
    private bool isBusy;
    private bool isInitializing = true;
    private double progressPercent;
    private string progressHeadline = string.Empty;
    private string progressDetail = string.Empty;
    private string progressStateLabel = string.Empty;
    private string elapsedTimeLabel = string.Empty;
    private string remainingTimeLabel = string.Empty;
    private string cpuName = string.Empty;
    private string gpuName = string.Empty;
    private string ramLabel = string.Empty;
    private string diskLabel = string.Empty;
    private string editionLabel = string.Empty;
    private string editionBadgeLabel = "AUTO";
    private string gtaStatusLabel = string.Empty;
    private string recommendationTitle = string.Empty;
    private string recommendationText = string.Empty;
    private string streamingProtectionTitle = string.Empty;
    private string streamingProtectionDetail = string.Empty;
    private string lightImpactLabel = string.Empty;
    private string balancedImpactLabel = string.Empty;
    private string aggressiveImpactLabel = string.Empty;
    private int readinessScore;
    private AppLanguagePreference languagePreference = AppLanguagePreference.Automatic;
    private AppThemePreference themePreference = AppThemePreference.System;
    private bool minimizeToTrayOnClose;
    private bool launchAtStartup;
    private bool checkForUpdates = true;
    private ReleaseUpdate? availableUpdate;
    private UpdatePresentationState updatePresentationState;
    private string? updateFailureMessage;
    private bool isUpdateDownloading;
    private double updateDownloadPercent;
    private string updateBannerTitle = string.Empty;
    private string updateBannerDetail = string.Empty;
    private long settingsRevision;
    private bool profileInitializedFromDiagnostic;
    private Stopwatch? operationStopwatch;
    private DispatcherTimer? operationTimer;

    public MainViewModel(
        IAppOptimizationService service,
        IPlanBuilder? planBuilder = null,
        ILocalizationService? localization = null,
        IStartupRegistrationService? startupRegistration = null,
        IReleaseUpdateService? releaseUpdateService = null)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.planBuilder = planBuilder ?? new PlanBuilder();
        this.localization = localization ?? LocalizationService.Current;
        this.startupRegistration = startupRegistration ?? new WindowsStartupRegistrationService();
        this.releaseUpdateService = releaseUpdateService;
        ResetLocalizedPlaceholders();
        ActivityLog.Add(new ActivityLogItem(
            DateTime.Now.ToString("HH:mm:ss"),
            this.localization.GetString("Log.StartedStandardUser")));
    }

    public ObservableCollection<ActionDisplayItem> PlannedActions { get; } = [];

    public ObservableCollection<ActivityLogItem> ActivityLog { get; } = [];

    public ObservableCollection<HistoryDisplayItem> HistoryItems { get; } = [];

    public string CpuName { get => cpuName; private set => SetProperty(ref cpuName, value); }

    public string GpuName { get => gpuName; private set => SetProperty(ref gpuName, value); }

    public string RamLabel { get => ramLabel; private set => SetProperty(ref ramLabel, value); }

    public string DiskLabel { get => diskLabel; private set => SetProperty(ref diskLabel, value); }

    public string EditionLabel { get => editionLabel; private set => SetProperty(ref editionLabel, value); }

    public string EditionBadgeLabel { get => editionBadgeLabel; private set => SetProperty(ref editionBadgeLabel, value); }

    public string GtaStatusLabel { get => gtaStatusLabel; private set => SetProperty(ref gtaStatusLabel, value); }

    public string RecommendationTitle { get => recommendationTitle; private set => SetProperty(ref recommendationTitle, value); }

    public string RecommendationText { get => recommendationText; private set => SetProperty(ref recommendationText, value); }

    public string StreamingProtectionTitle { get => streamingProtectionTitle; private set => SetProperty(ref streamingProtectionTitle, value); }

    public string StreamingProtectionDetail { get => streamingProtectionDetail; private set => SetProperty(ref streamingProtectionDetail, value); }

    public string LightImpactLabel { get => lightImpactLabel; private set => SetProperty(ref lightImpactLabel, value); }

    public string BalancedImpactLabel { get => balancedImpactLabel; private set => SetProperty(ref balancedImpactLabel, value); }

    public string AggressiveImpactLabel { get => aggressiveImpactLabel; private set => SetProperty(ref aggressiveImpactLabel, value); }

    public int ReadinessScore { get => readinessScore; private set => SetProperty(ref readinessScore, value); }

    public double ProgressPercent { get => progressPercent; private set => SetProperty(ref progressPercent, value); }

    public string ProgressHeadline { get => progressHeadline; private set => SetProperty(ref progressHeadline, value); }

    public string ProgressDetail { get => progressDetail; private set => SetProperty(ref progressDetail, value); }

    public string ProgressStateLabel { get => progressStateLabel; private set => SetProperty(ref progressStateLabel, value); }

    public string ElapsedTimeLabel { get => elapsedTimeLabel; private set => SetProperty(ref elapsedTimeLabel, value); }

    public string RemainingTimeLabel { get => remainingTimeLabel; private set => SetProperty(ref remainingTimeLabel, value); }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                RaiseCommandState();
            }
        }
    }

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

    public AppThemePreference ThemePreference => themePreference;

    public AppLanguagePreference LanguagePreference => languagePreference;

    public AppLanguage CurrentLanguage => localization.CurrentLanguage;

    public bool IsEnglishSelected => CurrentLanguage == AppLanguage.English;

    public bool IsPortugueseSelected => CurrentLanguage == AppLanguage.PortugueseBrazil;

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
            catch (Exception exception)
            {
                AddLog(localization.Format("Log.StartupSettingFailed", exception.Message));
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

    public bool IsUpdateBannerVisible => availableUpdate is not null
        || updatePresentationState == UpdatePresentationState.Failed;

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

    public bool CanDownloadUpdate => availableUpdate is not null && !IsUpdateDownloading;

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

    public string UpdateActionLabel => localization.GetString(
        updatePresentationState == UpdatePresentationState.Ready
            ? "Update.OpenInstaller"
            : "Update.Download");

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

    public string SelectedProfileLabel => selectedProfile switch
    {
        OptimizationProfile.Light => localization.GetString("Profiles.Light.Name").ToUpper(localization.CurrentCulture),
        OptimizationProfile.Balanced => $"{localization.GetString("Profiles.Balanced.Name").ToUpper(localization.CurrentCulture)} • {localization.GetString("Profiles.Balanced.Badge")}",
        OptimizationProfile.Aggressive => localization.GetString("Profiles.Aggressive.Name").ToUpper(localization.CurrentCulture),
        _ => localization.GetString("Common.Unknown").ToUpper(localization.CurrentCulture)
    };

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

            ApplySettings(await settingsTask);
            ApplyDiagnostic(await diagnosticTask);
            ApplyHistory(await historyTask);
            AddLog(localization.GetString("Log.DiagnosisCompleted"));
            if (checkForUpdates && releaseUpdateService is not null)
            {
                _ = CheckForUpdatesAsync();
            }
        }
        catch (Exception exception)
        {
            RecommendationTitle = localization.GetString("Diagnosis.Partial");
            RecommendationText = exception.Message;
            AddLog(localization.Format("Log.Warning", exception.Message));
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
            AddLog(localization.GetString("Log.ComputerScannedAgain"));
        }
        catch (Exception exception)
        {
            RecommendationTitle = localization.GetString("Diagnosis.CouldNotScanAgain");
            RecommendationText = exception.Message;
            AddLog(localization.Format("Log.RescanFailed", exception.Message));
        }
        finally
        {
            isInitializing = false;
            RefreshPlan();
            RaiseCommandState();
        }
    }

    public async Task CheckForUpdatesAsync()
    {
        if (releaseUpdateService is null || availableUpdate is not null)
        {
            return;
        }

        try
        {
            var assemblyVersion = Assembly.GetEntryAssembly()?.GetName().Version
                ?? new Version(0, 0, 0);
            var update = await releaseUpdateService.CheckForUpdateAsync(
                StableSemanticVersion.FromVersion(assemblyVersion));
            if (update is null)
            {
                return;
            }

            availableUpdate = update;
            updatePresentationState = UpdatePresentationState.Available;
            RefreshUpdatePresentation();
            AddLog(localization.Format("Log.UpdateAvailable", update.Version.CoreVersion));
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            // Falha de rede na inicialização não interrompe diagnóstico nem otimização.
            AddLog(localization.Format("Log.UpdateCheckFailed", exception.Message));
        }
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
            AddLog(localization.GetString("Log.UpdateVerified"));
            return downloaded;
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            updateFailureMessage = exception.Message;
            updatePresentationState = UpdatePresentationState.Failed;
            RefreshUpdatePresentation();
            AddLog(localization.Format("Log.UpdateDownloadFailed", exception.Message));
            return null;
        }
        finally
        {
            IsUpdateDownloading = false;
        }
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
        if (!Enum.IsDefined(language) || CurrentLanguage == language)
        {
            return;
        }

        localization.SetLanguage(language);
        languagePreference = language switch
        {
            AppLanguage.English => AppLanguagePreference.English,
            AppLanguage.PortugueseBrazil => AppLanguagePreference.PortugueseBrazil,
            _ => AppLanguagePreference.English
        };
        RefreshLocalizedState();
        SettingsChanged(refreshPlan: false);
    }

    public async Task StartOptimizationAsync()
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
            var block = currentPlan?.Blocks.FirstOrDefault();
            ProgressDetail = block is null
                ? localization.GetString("Plan.RunDiagnosisAgain")
                : LocalizeBlock(block);
            return;
        }

        operationCancellation = new CancellationTokenSource();
        IsBusy = true;
        ProgressPercent = 0;
        ProgressStateLabel = localization.GetString("Status.Preparing");
        StartOperationTiming();
        ActivityLog.Clear();
        AddLog(localization.Format("Log.StartingProfile", SelectedProfileLabel.ToLower(localization.CurrentCulture)));
        foreach (var notice in currentPlan.Notices.Where(item =>
                     item.Severity == PlanNoticeSeverity.Warning))
        {
            AddLog(localization.Format("Log.Warning", LocalizeNotice(notice)));
        }

        var progress = new Progress<AppProgressUpdate>(ApplyProgress);
        var completedSuccessfully = false;
        try
        {
            var result = await service.ExecuteAsync(currentPlan, progress, operationCancellation.Token);
            completedSuccessfully = result.Succeeded;
            ProgressPercent = result.Succeeded ? 100 : ProgressPercent;
            ProgressStateLabel = result.Succeeded
                ? localization.GetString("Status.Completed")
                : result.WasCancelled
                    ? localization.GetString("Status.Cancelled")
                    : localization.GetString("Status.Warning");
            ProgressHeadline = result.Succeeded
                ? localization.GetString("Status.OptimizationCompleted")
                : result.Summary;
            ProgressDetail = result.BytesFreed > 0
                ? localization.Format(
                    "Plan.ActionsCompletedFreed",
                    result.CompletedActions,
                    FormatBytes(result.BytesFreed))
                : localization.Format(
                    "Plan.ActionsCompletedSummary",
                    result.CompletedActions,
                    result.Summary);
            AddLog(result.Summary);
            ApplyHistory(await service.LoadHistoryAsync());
        }
        catch (OperationCanceledException)
        {
            ProgressStateLabel = localization.GetString("Status.Cancelled");
            ProgressHeadline = localization.GetString("Status.SafeCancellation.Headline");
            ProgressDetail = localization.GetString("Status.SafeCancellation.Detail");
            AddLog(localization.GetString("Log.CancellationConfirmed"));
        }
        catch (Exception exception)
        {
            ProgressStateLabel = localization.GetString("Status.SafeFailure");
            ProgressHeadline = localization.GetString("Status.CouldNotComplete");
            ProgressDetail = exception.Message;
            AddLog(localization.Format("Log.Error", exception.Message));
        }
        finally
        {
            StopOperationTiming(completedSuccessfully);
            operationCancellation.Dispose();
            operationCancellation = null;
            IsBusy = false;
        }
    }

    public void CancelOptimization()
    {
        if (operationCancellation is null)
        {
            return;
        }

        ProgressStateLabel = localization.GetString("Status.Cancelling");
        ProgressDetail = localization.GetString("Status.CancellationPending");
        operationCancellation.Cancel();
        RaiseCommandState();
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
        StartOperationTiming();
        var progress = new Progress<AppProgressUpdate>(ApplyProgress);
        var completedSuccessfully = false;
        try
        {
            var restored = await service.RollbackAsync(item.TransactionId, progress, operationCancellation.Token);
            completedSuccessfully = restored;
            ApplyHistory(await service.LoadHistoryAsync());
            AddLog(localization.GetString(
                restored ? "Log.RollbackCompleted" : "Log.NoReversibleChanges"));
            return restored;
        }
        catch (OperationCanceledException)
        {
            AddLog(localization.GetString("Log.RollbackCancelled"));
            return false;
        }
        catch (Exception exception)
        {
            ProgressStateLabel = localization.GetString("Status.SafeFailure");
            ProgressHeadline = localization.GetString("Status.CouldNotRestore");
            ProgressDetail = exception.Message;
            AddLog(localization.Format("Log.RollbackFailed", exception.Message));
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
        if (!profileInitializedFromDiagnostic)
        {
            selectedProfile = value.RecommendedProfile;
            profileInitializedFromDiagnostic = true;
            OnPropertyChanged(nameof(IsLightSelected));
            OnPropertyChanged(nameof(IsBalancedSelected));
            OnPropertyChanged(nameof(IsAggressiveSelected));
            OnPropertyChanged(nameof(SelectedProfileLabel));
            OnPropertyChanged(nameof(SelectedProfileName));
        }

        CpuName = value.CpuName;
        GpuName = value.GpuName;
        RamLabel = localization.Format(
            "Diagnosis.MemoryAvailable",
            value.TotalMemoryGiB,
            value.AvailableMemoryGiB);
        DiskLabel = localization.Format("Diagnosis.FreeDisk", value.FreeDiskGiB);
        ReadinessScore = value.ReadinessScore;
        EditionLabel = value.Edition switch
        {
            FiveMEdition.Legacy => localization.GetString("Diagnosis.FiveMLegacyDetected"),
            FiveMEdition.Enhanced => localization.GetString("Diagnosis.FiveMEnhancedBlocked"),
            _ => localization.GetString("Diagnosis.FiveMNotFound")
        };
        EditionBadgeLabel = value.Edition switch
        {
            FiveMEdition.Legacy => "LEGACY",
            FiveMEdition.Enhanced => "ENHANCED",
            _ => localization.GetString("Status.Waiting")
        };
        GtaStatusLabel = value.GtaVIsRunning
            ? localization.GetString("Diagnosis.GtaVOpen")
            : value.GtaVDetected
            ? localization.GetString("Diagnosis.GtaVLegacyDetected")
            : File.Exists(value.GtaVGraphicsSettingsPath)
                ? localization.GetString("Diagnosis.GtaVSettingsDetected")
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
        ApplyStreamingProtection(value.StreamingSoftware);
        ApplyProfileImpact(value.PerformancePressure);
    }

    private void ApplyProfileImpact(PerformancePressureLevel pressure)
    {
        var suffix = pressure switch
        {
            PerformancePressureLevel.Low => "Low",
            PerformancePressureLevel.Moderate => "Moderate",
            PerformancePressureLevel.High => "High",
            _ => "Moderate"
        };
        LightImpactLabel = localization.GetString($"Profiles.Impact.Light.{suffix}");
        BalancedImpactLabel = localization.GetString($"Profiles.Impact.Balanced.{suffix}");
        AggressiveImpactLabel = localization.GetString($"Profiles.Impact.Aggressive.{suffix}");
    }

    private void ApplyStreamingProtection(StreamingSoftwareSnapshot snapshot)
    {
        var running = snapshot.Applications
            .Where(item => item.IsProcessRunning)
            .Select(item => item.DisplayName)
            .ToArray();
        if (running.Length > 0)
        {
            StreamingProtectionTitle = localization.Format(
                "Streaming.RunningTitle",
                string.Join(", ", running));
            StreamingProtectionDetail = localization.GetString("Streaming.RunningDetail");
            return;
        }

        var installed = snapshot.Applications
            .Where(item => item.IsInstalled)
            .Select(item => item.DisplayName)
            .ToArray();
        if (installed.Length > 0)
        {
            StreamingProtectionTitle = localization.Format(
                "Streaming.InstalledTitle",
                string.Join(", ", installed));
            StreamingProtectionDetail = localization.GetString("Streaming.InstalledDetail");
            return;
        }

        if (snapshot.IsPartial)
        {
            StreamingProtectionTitle = localization.GetString("Streaming.PartialTitle");
            StreamingProtectionDetail = localization.GetString("Streaming.PartialDetail");
            return;
        }

        StreamingProtectionTitle = localization.GetString("Streaming.SafeTitle");
        StreamingProtectionDetail = localization.GetString("Streaming.SafeDetail");
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
        try
        {
            launchAtStartup = startupRegistration.IsEnabled();
        }
        catch (Exception exception)
        {
            launchAtStartup = settings.LaunchAtStartup;
            AddLog(localization.Format("Log.StartupReadFailed", exception.Message));
        }

        OnPropertyChanged(nameof(LanguagePreference));
        OnPropertyChanged(nameof(CurrentLanguage));
        OnPropertyChanged(nameof(IsEnglishSelected));
        OnPropertyChanged(nameof(IsPortugueseSelected));
        OnPropertyChanged(nameof(ThemePreference));
        OnPropertyChanged(nameof(IsSystemThemeSelected));
        OnPropertyChanged(nameof(IsDarkThemeSelected));
        OnPropertyChanged(nameof(IsLightThemeSelected));
        OnPropertyChanged(nameof(MinimizeToTrayOnClose));
        OnPropertyChanged(nameof(LaunchAtStartup));
        OnPropertyChanged(nameof(CheckForUpdates));
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

        if (HistoryItems.Count == 0)
        {
            HistoryItems.Add(new HistoryDisplayItem(
                Guid.Empty,
                localization.GetString("History.Empty.Title"),
                localization.GetString("History.Empty.Date"),
                localization.GetString("History.Empty.Summary"),
                false));
        }
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

        currentPlan = planBuilder.Build(new OptimizationPlanRequestDto
        {
            Profile = selectedProfile,
            Edition = edition,
            Options = options
        });

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
        RaiseCommandState();
    }

    private void SettingsChanged(bool refreshPlan = true)
    {
        if (refreshPlan)
        {
            RefreshPlan();
        }

        var revision = Interlocked.Increment(ref settingsRevision);
        var snapshot = new AppSettings
        {
            Language = languagePreference,
            Theme = ThemePreference,
            MinimizeToTrayOnClose = MinimizeToTrayOnClose,
            LaunchAtStartup = LaunchAtStartup,
            CheckForUpdates = CheckForUpdates
        };
        _ = SaveSettingsRevisionAsync(snapshot, revision);
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
        catch
        {
            AddLog(localization.GetString("Log.SettingsSaveFailed"));
        }
    }

    private void ApplyProgress(AppProgressUpdate update)
    {
        ProgressPercent = Math.Clamp(update.Percent, 0, 100);
        ProgressHeadline = update.Headline;
        ProgressDetail = update.Detail;
        ProgressStateLabel = update.Kind switch
        {
            AppProgressKind.Preparing => localization.GetString("Status.Preparing"),
            AppProgressKind.Applying => localization.GetString("Status.Optimizing"),
            AppProgressKind.Verifying => localization.GetString("Status.Verifying"),
            AppProgressKind.RollingBack => localization.GetString("Status.Restoring"),
            AppProgressKind.Completed => localization.GetString("Status.Completed"),
            AppProgressKind.Warning => localization.GetString("Status.Warning"),
            AppProgressKind.Failed => localization.GetString("Status.SafeFailure"),
            _ => localization.GetString("Status.InProgress")
        };
        UpdateOperationTiming();
        AddLog(update.Detail);
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
            ProgressDetail = localization.GetString("Status.Ready.Detail");
            ProgressStateLabel = localization.GetString("Status.Waiting");
            ElapsedTimeLabel = localization.Format("Progress.ElapsedFormat", "00:00");
            RemainingTimeLabel = localization.GetString("Progress.Calculating");
        }

        if (!preserveDiagnostic || diagnostic is null)
        {
            var analyzing = localization.GetString("Status.Analyzing");
            CpuName = analyzing;
            GpuName = analyzing;
            RamLabel = analyzing;
            DiskLabel = analyzing;
            EditionLabel = localization.GetString("Status.SearchingFiveM");
            GtaStatusLabel = localization.GetString("Status.SearchingGtaV");
            RecommendationTitle = localization.GetString("Status.AnalyzingComputer");
            RecommendationText = localization.GetString("Status.LocalOnly");
            StreamingProtectionTitle = localization.GetString("Streaming.SafeTitle");
            StreamingProtectionDetail = localization.GetString("Streaming.SafeDetail");
            var pendingImpact = localization.GetString("Profiles.Impact.Pending");
            LightImpactLabel = pendingImpact;
            BalancedImpactLabel = pendingImpact;
            AggressiveImpactLabel = pendingImpact;
        }
    }

    private void RefreshLocalizedState()
    {
        OnPropertyChanged(nameof(LanguagePreference));
        OnPropertyChanged(nameof(CurrentLanguage));
        OnPropertyChanged(nameof(IsEnglishSelected));
        OnPropertyChanged(nameof(IsPortugueseSelected));
        OnPropertyChanged(nameof(SelectedProfileLabel));
        OnPropertyChanged(nameof(SelectedProfileName));
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

    private void AddLog(string message)
    {
        ActivityLog.Add(new ActivityLogItem(DateTime.Now.ToString("HH:mm:ss"), message));
        while (ActivityLog.Count > 100)
        {
            ActivityLog.RemoveAt(0);
        }
    }

    private void RaiseCommandState()
    {
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanCancel));
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
            case UpdatePresentationState.Failed:
                UpdateBannerTitle = localization.GetString("Update.Failed.Title");
                UpdateBannerDetail = localization.Format(
                    "Update.Failed.Detail",
                    updateFailureMessage ?? localization.GetString("Common.Unknown"));
                break;
        }

        OnPropertyChanged(nameof(IsUpdateBannerVisible));
        OnPropertyChanged(nameof(UpdateActionLabel));
        OnPropertyChanged(nameof(CanDownloadUpdate));
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
        var privilege = action.RequiredPrivilege == RequiredPrivilege.Administrator
            ? localization.GetString("Privilege.RequiresUac")
            : action.Reversibility is ActionReversibility.Irreversible or ActionReversibility.RebuildableData
                ? localization.GetString("Privilege.PermanentCleanup")
                : localization.GetString("Privilege.Reversible");
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
            privilege);
    }

    private string LocalizeBlock(PlanBlockDto block) => block.Code switch
    {
        PlanBlockCode.EditionNotDetected => localization.GetString("Plan.Notice.NoLegacy"),
        PlanBlockCode.EnhancedNotSupported => localization.GetString("Plan.Notice.EnhancedUnsupported"),
        _ => block.Message
    };

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

    private static string FormatBytes(long bytes)
    {
        const double giB = 1024d * 1024d * 1024d;
        const double miB = 1024d * 1024d;
        return bytes >= giB ? $"{bytes / giB:0.##} GB" : $"{bytes / miB:0.#} MB";
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
        Failed
    }
}
