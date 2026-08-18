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

public sealed partial class MainViewModel
{
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

    /// <summary>
    /// Decision computed by <see cref="ReleaseNotesEvaluator"/> from the
    /// settings just loaded in <see cref="InitializeAsync"/>, analogous to
    /// <see cref="PrivacyConsentDecision"/>. The window (view responsibility)
    /// decides whether and what to show from this value alone.
    /// </summary>
    public ReleaseNotesDecision? PendingReleaseNotes { get; private set; }

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
        dismissedLiveAlertId = settings.DismissedLiveAlertId;
        lastSeenReleaseNotesVersion = settings.LastSeenReleaseNotesVersion;
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

    private AppSettings BuildSettingsSnapshot() => new()
    {
        Language = languagePreference,
        Theme = ThemePreference,
        MinimizeToTrayOnClose = MinimizeToTrayOnClose,
        LaunchAtStartup = LaunchAtStartup,
        CheckForUpdates = CheckForUpdates,
        ShareAnonymousTelemetry = ShareAnonymousTelemetry,
        ShareCrashReports = ShareCrashReports,
        PrivacyConsentVersion = privacyConsentVersion,
        DismissedLiveAlertId = dismissedLiveAlertId,
        LastSeenReleaseNotesVersion = lastSeenReleaseNotesVersion
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

    /// <summary>
    /// Records <paramref name="version"/> as the last release notes version
    /// the user has seen (or silently acknowledged — see
    /// <see cref="ReleaseNotesDecision.ShouldRecordSilently"/>), through the
    /// same settings persistence path as every other preference. The caller
    /// (<c>MainWindow</c>) only invokes this after the "What's New" panel
    /// has actually been closed, or immediately for the silent cases, so a
    /// crash before the panel is dismissed does not mark unseen notes as
    /// seen.
    /// </summary>
    public async Task ConfirmReleaseNotesSeenAsync(string version)
    {
        lastSeenReleaseNotesVersion = version;
        PendingReleaseNotes = null;

        var revision = Interlocked.Increment(ref settingsRevision);
        await SaveSettingsRevisionAsync(BuildSettingsSnapshot(), revision).ConfigureAwait(false);
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
            PerformancePressureBrushKey = "TextTertiaryBrush";
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
}
