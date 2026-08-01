using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FiveMCleaner.App.Services;
using FiveMCleaner.App.ViewModels;
using FiveMCleaner.App.Views;
using FiveMCleaner.Contracts;
using FiveMCleaner.UpdateRuntime;

namespace FiveMCleaner.App;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private const uint MonitorDefaultToNearest = 2;
    private const int WmGetMinMaxInfo = 0x0024;
    private readonly MainViewModel viewModel;
    private readonly ThemeManager themeManager;
    private readonly TrayIconService trayIcon;
    private readonly IReleaseUpdateService? releaseUpdateService;
    private readonly bool startupLaunch;
    private readonly bool demoMode;
    private readonly RemoteServicesOptions remoteServicesOptions;
    private readonly QueuedCloudflareTelemetryService? queuedCloudflareTelemetry;
    private HwndSource? windowSource;
    private bool allowClose;
    private bool closeAfterOptimizationStops;
    private bool trayAnnouncementShown;
    private bool systemSessionEnding;

    public MainWindow()
    {
        InitializeComponent();
        themeManager = new ThemeManager();
        themeManager.Apply(AppThemePreference.System);
        var commandLine = Environment.GetCommandLineArgs();
        var syntheticDemo = commandLine
            .Any(value => value.Equals("--demo-synthetic", StringComparison.OrdinalIgnoreCase));
        demoMode = syntheticDemo || commandLine
            .Any(value => value.Equals("--demo", StringComparison.OrdinalIgnoreCase));
        startupLaunch = commandLine
            .Any(value => value.Equals("--startup", StringComparison.OrdinalIgnoreCase));
        var justUpdatedVersion = commandLine
            .FirstOrDefault(value => value.StartsWith("--updated=", StringComparison.OrdinalIgnoreCase))
            ?["--updated=".Length..];
        var runtimeLayout = RuntimeLayout.Resolve(AppContext.BaseDirectory);
        var installRoot = runtimeLayout.InstallRoot;
        var runtimeRoot = runtimeLayout.RuntimeRoot;
        IStartupRegistrationService startupRegistration = demoMode
            ? new SessionStartupRegistrationService()
            : runtimeRoot is null
                ? new WindowsStartupRegistrationService()
                : new WindowsStartupRegistrationService(
                    Path.Combine(installRoot!, "FiveMCleaner.Launcher.exe"));
        releaseUpdateService = demoMode
            ? null
            : runtimeRoot is null ? new GitHubReleaseUpdateService() : new SignedManifestUpdateService();
        ISilentUpdateInstaller? silentUpdateInstaller = demoMode ? null
            : runtimeRoot is not null
                ? new AtomicUpdateInstaller(runtimeRoot, Path.Combine(installRoot!, "FiveMCleaner.Launcher.exe"))
                : new SilentUpdateInstaller(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    ProductIdentity.Name,
                    "Updates"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    ProductIdentity.Name,
                    "Logs"),
                Path.Combine(AppContext.BaseDirectory, "updater", "FiveMCleaner.Updater.exe"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    ProductIdentity.Name,
                    "Updater"));
        var runtimeEnvironment = AppEnvironment.Resolve();
        remoteServicesOptions = RemoteServicesOptionsLoader.Load(runtimeEnvironment, AppContext.BaseDirectory);
        // Cloudflare is the sole telemetry transport (FormSubmit was
        // removed entirely). If the configured endpoint is ever missing or
        // malformed, telemetry safely does nothing rather than crash or
        // silently fall back to a different destination.
        IAnonymousTelemetryService telemetryService;
        if (demoMode)
        {
            telemetryService = DisabledAnonymousTelemetryService.Instance;
        }
        else if (TelemetryEndpointPolicy.TryCreate(
            remoteServicesOptions.TelemetryEndpoint,
            runtimeEnvironment,
            out var telemetryEndpoint,
            out _))
        {
            queuedCloudflareTelemetry = new QueuedCloudflareTelemetryService(
                new LocalTelemetryQueue(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    ProductIdentity.Name,
                    "Telemetry",
                    "pending")),
                new CloudflareTelemetryTransport(telemetryEndpoint, remoteServicesOptions.Environment));
            telemetryService = queuedCloudflareTelemetry;
        }
        else
        {
            telemetryService = DisabledAnonymousTelemetryService.Instance;
        }

        viewModel = new MainViewModel(
            new AppOptimizationService(demoMode, syntheticDemo),
            localization: LocalizationService.Current,
            startupRegistration: startupRegistration,
            releaseUpdateService: releaseUpdateService,
            telemetry: telemetryService,
            silentUpdateInstaller: silentUpdateInstaller);
        if (!string.IsNullOrWhiteSpace(justUpdatedVersion))
        {
            viewModel.ReportCompletedUpdate(justUpdatedVersion);
        }
        trayIcon = new TrayIconService(LocalizationService.Current);
        trayIcon.ShowRequested += TrayIcon_ShowRequested;
        trayIcon.ExitRequested += TrayIcon_ExitRequested;
        viewModel.UpdateAvailableDetected += ViewModel_UpdateAvailableDetected;
        DataContext = viewModel;
        Loaded += MainWindow_Loaded;
        SourceInitialized += MainWindow_SourceInitialized;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        System.Windows.Application.Current.SessionEnding += Application_SessionEnding;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ActivateNavItem(DashboardNav);
        Navigate(DashboardPage);
        if (!demoMode)
        {
            // O recibo de saúde precisa ser gravado antes do InitializeAsync:
            // a janela de saúde do launcher (45s) começa no spawn do processo,
            // e a inicialização (varredura WMI/registro, flush de telemetria,
            // checagem de update) pode passar disso em máquinas lentas. Um
            // candidato saudável, apenas lento, não deve ser revertido -- o
            // recibo confirma "o processo iniciou e a interface respondeu",
            // não "todo o trabalho em segundo plano terminou".
            ConfirmUpdateHealthIfRequested();
        }

        await viewModel.InitializeAsync();
        themeManager.Apply(viewModel.ThemePreference);
        LanguageSelector.SelectedIndex = viewModel.IsPortugueseSelected
            ? 0
            : viewModel.IsSpanishSelected ? 2 : 1;
        ThemeSelector.SelectedIndex = viewModel.ThemePreference switch
        {
            AppThemePreference.Dark => 1,
            AppThemePreference.Light => 2,
            _ => 0
        };
        if (!demoMode)
        {
            await ShowPrivacyConsentIfNeededAsync();
            InitializeCrashReportingIfAuthorized();
            await FlushPendingTelemetryIfAnyAsync();
        }
        if (startupLaunch && viewModel.MinimizeToTrayOnClose)
        {
            HideToTray();
        }
        await CaptureIfRequestedAsync();
    }

    private static void ConfirmUpdateHealthIfRequested()
    {
        var arguments = Environment.GetCommandLineArgs();
        var transaction = arguments.FirstOrDefault(value => value.StartsWith("--update-transaction=", StringComparison.OrdinalIgnoreCase))?["--update-transaction=".Length..];
        var nonce = arguments.FirstOrDefault(value => value.StartsWith("--update-nonce=", StringComparison.OrdinalIgnoreCase))?["--update-nonce=".Length..];
        var runtimeRoot = RuntimeLayout.Resolve(AppContext.BaseDirectory).RuntimeRoot;
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3);
        if (version is null || runtimeRoot is null) return;
        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductIdentity.Name);
        if (transaction is not null && nonce is not null)
        {
            try { new UpdateHealthReceiptStore(runtimeRoot).Confirm(transaction, version, nonce); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
            {
                // A falha preserva o app ativo; o launcher apenas não vê o
                // recibo de saúde neste momento e re-verifica na próxima
                // abertura antes de qualquer rollback.
            }
        }
        try { new VersionFloorStore(dataRoot).Advance(version); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            // A falha preserva o app ativo; a próxima consulta de update falha
            // fechada ao não conseguir validar o piso DPAPI.
        }
    }

    /// <summary>
    /// Shows the blocking privacy consent screen when
    /// <see cref="MainViewModel.PrivacyConsentDecision"/> (computed once,
    /// right after settings finish loading in
    /// <see cref="MainViewModel.InitializeAsync"/>) says a decision is still
    /// pending. Runs before anything else in <see cref="MainWindow_Loaded"/>
    /// so the main window is shown but not meaningfully usable — the modal
    /// dialog blocks input to it — until the user confirms or closes it.
    /// Demo mode never shows this screen: it never persists settings or
    /// sends telemetry regardless, and smoke tests must not hang on a modal.
    /// </summary>
    private async Task ShowPrivacyConsentIfNeededAsync()
    {
        var decision = viewModel.PrivacyConsentDecision;
        if (decision is null || !decision.RequiresConsentScreen)
        {
            return;
        }

        var consentWindow = new PrivacyConsentWindow(
            decision.Variant,
            viewModel.ShareAnonymousTelemetry,
            viewModel.ShareCrashReports)
        {
            Owner = this
        };
        consentWindow.ShowDialog();
        await viewModel.ConfirmPrivacyConsentAsync(
            consentWindow.AcceptedAnonymousTelemetry,
            consentWindow.AcceptedCrashReports);
    }

    /// <summary>
    /// Initializes the real Sentry-backed crash reporter, but only after
    /// <see cref="ShowPrivacyConsentIfNeededAsync"/> has resolved: by that
    /// point <see cref="MainViewModel.ShareCrashReports"/> is guaranteed to
    /// reflect a current, confirmed consent (either it already was current,
    /// or the consent window just made it so) — so this single check is
    /// enough, no separate re-evaluation of
    /// <see cref="MainViewModel.PrivacyConsentDecision"/> is needed. Loads
    /// the Sentry DSN from the environment-specific config file
    /// (<see cref="RemoteServicesOptionsLoader"/>) — never from a literal in
    /// source — and tags the event with the resolved
    /// <see cref="AppEnvironment"/> so development and production errors are
    /// never mixed together in Sentry.
    /// </summary>
    private void InitializeCrashReportingIfAuthorized()
    {
        if (!viewModel.ShareCrashReports)
        {
            return;
        }

        CrashReporting.Current = new SentryCrashReportingService();
        CrashReporting.Current.Initialize(remoteServicesOptions, viewModel.AppVersion);
    }

    /// <summary>
    /// Retries sending whatever telemetry could not be delivered during a
    /// previous, possibly offline, run. A no-op unless the Cloudflare
    /// transport is the active one (<see cref="queuedCloudflareTelemetry"/>
    /// is only set when a telemetry endpoint is configured); FormSubmit has
    /// no queue to flush.
    /// </summary>
    private Task FlushPendingTelemetryIfAnyAsync() =>
        queuedCloudflareTelemetry?.FlushPendingAsync() ?? Task.CompletedTask;

    private static bool TryCreateHttpsEndpoint(string? value, out Uri endpoint)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && Uri.TryCreate(value, UriKind.Absolute, out var candidate)
            && candidate.Scheme == Uri.UriSchemeHttps)
        {
            endpoint = candidate;
            return true;
        }

        endpoint = null!;
        return false;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        windowSource = PresentationSource.FromVisual(this) as HwndSource;
        windowSource?.AddHook(WindowMessageHook);
    }

    private IntPtr WindowMessageHook(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message != WmGetMinMaxInfo)
        {
            return IntPtr.Zero;
        }

        var monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            return IntPtr.Zero;
        }

        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        minMaxInfo.MaxPosition = new NativePoint(
            monitorInfo.WorkArea.Left - monitorInfo.MonitorArea.Left,
            monitorInfo.WorkArea.Top - monitorInfo.MonitorArea.Top);
        minMaxInfo.MaxSize = new NativePoint(
            monitorInfo.WorkArea.Right - monitorInfo.WorkArea.Left,
            monitorInfo.WorkArea.Bottom - monitorInfo.WorkArea.Top);
        minMaxInfo.MaxTrackSize = minMaxInfo.MaxSize;
        Marshal.StructureToPtr(minMaxInfo, lParam, false);
        handled = true;
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRectangle MonitorArea;
        public NativeRectangle WorkArea;
        public uint Flags;
    }

    private void NavItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.NavigationViewItem { Tag: string tag } item)
        {
            return;
        }

        ActivateNavItem(item);
        Navigate(tag switch
        {
            "Optimizer" => OptimizerPage,
            "History" => HistoryPage,
            "Settings" => SettingsPage,
            _ => DashboardPage
        });
    }

    private void ActivateNavItem(Wpf.Ui.Controls.NavigationViewItem selected)
    {
        DashboardNav.IsActive = ReferenceEquals(selected, DashboardNav);
        OptimizerNav.IsActive = ReferenceEquals(selected, OptimizerNav);
        HistoryNav.IsActive = ReferenceEquals(selected, HistoryNav);
        SettingsNav.IsActive = ReferenceEquals(selected, SettingsNav);
    }

    private void ReviewPlan_Click(object sender, RoutedEventArgs e)
    {
        ActivateNavItem(OptimizerNav);
        Navigate(OptimizerPage);
        PlanDetailsExpander.IsExpanded = true;
        PlanDetailsExpander.BringIntoView();
    }

    private void OpenOptimizer_Click(object sender, RoutedEventArgs e)
    {
        ActivateNavItem(OptimizerNav);
        Navigate(OptimizerPage);
    }

    private void ChangeMode_Click(object sender, RoutedEventArgs e) => ProfileSelectorSection.BringIntoView();

    private void Navigate(UIElement page)
    {
        DashboardPage.Visibility = Visibility.Collapsed;
        OptimizerPage.Visibility = Visibility.Collapsed;
        HistoryPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        viewModel.SetLiveMetricsEnabled(ReferenceEquals(page, DashboardPage));
    }

    private void LightProfile_Checked(object sender, RoutedEventArgs e) => viewModel.SelectProfile(OptimizationProfile.Light);

    private void BalancedProfile_Checked(object sender, RoutedEventArgs e) => viewModel.SelectProfile(OptimizationProfile.Balanced);

    private void AggressiveProfile_Checked(object sender, RoutedEventArgs e) => viewModel.SelectProfile(OptimizationProfile.Aggressive);

    private void SystemTheme_Checked(object sender, RoutedEventArgs e) => ApplyTheme(AppThemePreference.System);

    private void DarkTheme_Checked(object sender, RoutedEventArgs e) => ApplyTheme(AppThemePreference.Dark);

    private void LightTheme_Checked(object sender, RoutedEventArgs e) => ApplyTheme(AppThemePreference.Light);

    private void EnglishLanguage_Checked(object sender, RoutedEventArgs e) => ApplyLanguage(AppLanguage.English);

    private void PortugueseLanguage_Checked(object sender, RoutedEventArgs e) => ApplyLanguage(AppLanguage.PortugueseBrazil);

    private void LanguageSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded || LanguageSelector.SelectedItem is not System.Windows.Controls.ComboBoxItem item)
        {
            return;
        }

        ApplyLanguage((item.Tag as string) switch
        {
            "pt-BR" => AppLanguage.PortugueseBrazil,
            "es" => AppLanguage.Spanish,
            _ => AppLanguage.English
        });
    }

    private void ThemeSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded || ThemeSelector.SelectedItem is not System.Windows.Controls.ComboBoxItem item)
        {
            return;
        }

        ApplyTheme((string?)item.Tag switch
        {
            "dark" => AppThemePreference.Dark,
            "light" => AppThemePreference.Light,
            _ => AppThemePreference.System
        });
    }

    private void CloseAppOnClose_Checked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            viewModel.MinimizeToTrayOnClose = false;
        }
    }

    private void MinimizeToTrayOnClose_Checked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            viewModel.MinimizeToTrayOnClose = true;
        }
    }

    private void ApplyTheme(AppThemePreference preference)
    {
        if (!IsLoaded)
        {
            return;
        }

        viewModel.SelectTheme(preference);
        themeManager.Apply(preference);
    }

    private void ApplyLanguage(AppLanguage language)
    {
        if (IsLoaded)
        {
            viewModel.SelectLanguage(language);
        }
    }

    private async void RefreshDiagnostic_Click(object sender, RoutedEventArgs e) => await viewModel.RefreshDiagnosticAsync();

    private async void StartOptimization_Click(object sender, RoutedEventArgs e)
    {
        ActivateNavItem(OptimizerNav);
        Navigate(OptimizerPage);
        await viewModel.StartOptimizationAsync();
        if (closeAfterOptimizationStops)
        {
            closeAfterOptimizationStops = false;
            allowClose = true;
            Close();
        }
    }

    /// <summary>
    /// The whole one-click update: confirm once, then the view model downloads
    /// the hash-verified installer and runs it silently. There is no second
    /// dialog and no installer wizard to click through — a successful launch
    /// means the installer is already running its own [Run] entry to relaunch
    /// FiveMCleaner, so this window closes immediately to let it replace the
    /// files. A failure at any point (download or install) leaves the app open
    /// with the banner explaining what happened.
    /// </summary>
    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.CanDownloadUpdate || viewModel.AvailableUpdateVersion is not { } pendingVersion)
        {
            return;
        }

        var decision = System.Windows.MessageBox.Show(
            LocalizationService.Current.Format("Dialog.UpdateInstall.Message", pendingVersion),
            LocalizationService.Current.GetString("Dialog.UpdateInstall.Title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        var installing = await viewModel.DownloadAndInstallUpdateAsync();
        if (!installing)
        {
            // The banner already shows the failure; the app stays open so the
            // user can retry or keep working on the current version.
            return;
        }

        allowClose = true;
        trayIcon.Hide();
        Close();
    }

    private void OpenReleaseNotes_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.ReleaseNotesUri is not { } releaseNotesUri)
        {
            return;
        }

        TryOpenExternal(() => Process.Start(new ProcessStartInfo
        {
            FileName = releaseNotesUri.AbsoluteUri,
            UseShellExecute = true
        }));
    }

    private void DismissCompletedUpdate_Click(object sender, RoutedEventArgs e) =>
        viewModel.DismissCompletedUpdateBanner();

    /// <summary>
    /// Shell launches fail for reasons outside the app's control (no default
    /// browser or folder handler registered, group policy blocking the verb,
    /// denied access to the folder). Unhandled, they reach
    /// DispatcherUnhandledException, which shuts FiveMCleaner down — closing
    /// the app over a failed "open this link" is never the right outcome.
    /// </summary>
    private static void TryOpenExternal(Action launch)
    {
        try
        {
            launch();
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            System.Windows.MessageBox.Show(
                LocalizationService.Current.Format(
                    "Dialog.OpenExternal.Message",
                    exception.Message),
                LocalizationService.Current.GetString("Dialog.OpenExternal.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void CancelOptimization_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.IsBusy || !ConfirmOptimizationInterruption(closeApplication: false))
        {
            return;
        }

        viewModel.CancelOptimization();
    }

    private void CopyTechnicalReport_Click(object sender, RoutedEventArgs e) => viewModel.CopyTechnicalReport();

    private void SaveTechnicalReport_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.CanShareReport)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = viewModel.SuggestedReportFileName,
            DefaultExt = ".txt",
            Filter = "Text (*.txt)|*.txt|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            viewModel.SaveTechnicalReport(dialog.FileName);
        }
    }

    private async void RunGtaVBenchmark_Click(object sender, RoutedEventArgs e) => await viewModel.RunGtaVBenchmarkAsync();

    private async void CheckForUpdatesManually_Click(object sender, RoutedEventArgs e) => await viewModel.CheckForUpdatesManuallyAsync();

    private async void RevertLastOptimization_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.CanRevertLastOptimization)
        {
            await viewModel.RevertLastOptimizationAsync();
        }
    }

    private async void RollbackHistory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: HistoryDisplayItem item } || !item.CanRollback)
        {
            return;
        }

        var decision = System.Windows.MessageBox.Show(
            LocalizationService.Current.GetString("Dialog.Rollback.Message"),
            LocalizationService.Current.GetString("Dialog.Rollback.Title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (decision == MessageBoxResult.Yes)
        {
            await viewModel.RollbackAsync(item);
        }
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        TryOpenExternal(() =>
        {
            Directory.CreateDirectory(viewModel.LogsDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{viewModel.LogsDirectory}\"",
                UseShellExecute = true
            });
        });
    }

    private void ReportBug_Click(object sender, RoutedEventArgs e)
    {
        IBugReportService bugReportService = TryCreateHttpsEndpoint(remoteServicesOptions.BugReportEndpoint, out var bugReportEndpoint)
            ? new CloudflareBugReportService(bugReportEndpoint, remoteServicesOptions.Environment)
            : new DisabledBugReportService();

        var dialog = new BugReportWindow(
            bugReportService,
            viewModel.AppVersion,
            viewModel.SelectedProfileName,
            viewModel.EditionBadgeLabel)
        {
            Owner = this
        };
        _ = dialog.ShowDialog();
    }

    private void OpenRepository_Click(object sender, RoutedEventArgs e)
    {
        TryOpenExternal(() => Process.Start(new ProcessStartInfo
        {
            FileName = ProductIdentity.RepositoryUrl,
            UseShellExecute = true
        }));
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (viewModel.IsBusy && !systemSessionEnding)
        {
            e.Cancel = true;
            if (ConfirmOptimizationInterruption(closeApplication: true))
            {
                closeAfterOptimizationStops = true;
                viewModel.CancelOptimization();
            }

            return;
        }

        if (!allowClose && viewModel.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            HideToTray();
        }
    }

    private bool ConfirmOptimizationInterruption(bool closeApplication)
    {
        var localization = LocalizationService.Current;
        var dialog = new OptimizationConfirmationWindow(
            localization.GetString(
                closeApplication
                    ? "Dialog.CloseOptimization.Title"
                    : "Dialog.CancelOptimization.Title"),
            localization.GetString(
                closeApplication
                    ? "Dialog.CloseOptimization.Message"
                    : "Dialog.CancelOptimization.Message"),
            localization.GetString("Dialog.OptimizationInterruption.KeepWorking"),
            localization.GetString(
                closeApplication
                    ? "Dialog.CloseOptimization.Confirm"
                    : "Dialog.CancelOptimization.Confirm"))
        {
            Owner = this
        };
        return dialog.ShowDialog() == true;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        viewModel.Dispose();
        windowSource?.RemoveHook(WindowMessageHook);
        System.Windows.Application.Current.SessionEnding -= Application_SessionEnding;
        viewModel.UpdateAvailableDetected -= ViewModel_UpdateAvailableDetected;
        themeManager.Dispose();
        trayIcon.Dispose();
        (releaseUpdateService as IDisposable)?.Dispose();
    }

    private void Application_SessionEnding(object? sender, SessionEndingCancelEventArgs e)
    {
        // Nunca transforma a preferência de bandeja em bloqueio de logoff/desligamento.
        systemSessionEnding = true;
        allowClose = true;
        viewModel.CancelOptimization();
    }

    private void HideToTray()
    {
        viewModel.SetLiveMetricsEnabled(false);
        Hide();
        trayIcon.Show(announce: !trayAnnouncementShown);
        trayAnnouncementShown = true;
    }

    private void ViewModel_UpdateAvailableDetected(object? sender, string version)
    {
        // Shows regardless of whether the window is currently visible,
        // minimized or minimized to the tray — the user asked for the
        // native Windows notification to fire in every case.
        trayIcon.ShowUpdateAvailable(version);
    }

    private void TrayIcon_ShowRequested(object? sender, EventArgs e)
    {
        trayIcon.Hide();
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Maximized;
        }

        Activate();
        viewModel.SetLiveMetricsEnabled(DashboardPage.Visibility == Visibility.Visible);
    }

    private void TrayIcon_ExitRequested(object? sender, EventArgs e)
    {
        allowClose = true;
        trayIcon.Hide();
        Close();
    }

    private async Task CaptureIfRequestedAsync()
    {
        var argument = Environment.GetCommandLineArgs()
            .FirstOrDefault(value => value.StartsWith("--capture=", StringComparison.OrdinalIgnoreCase));
        if (argument is null)
        {
            return;
        }

        try
        {
            var outputPath = Path.GetFullPath(argument["--capture=".Length..].Trim('"'));
            await Task.Delay(450);
            UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(this);
            var bitmap = new RenderTargetBitmap(
                Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX)),
                Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY)),
                dpi.PixelsPerInchX,
                dpi.PixelsPerInchY,
                PixelFormats.Pbgra32);
            bitmap.Render(this);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await using var stream = File.Create(outputPath);
            encoder.Save(stream);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // O modo --capture= é um smoke-test: um caminho inválido ou um
            // disco cheio não pode transformar a captura em um crash da UI.
            // Sem o arquivo de saída, o script que orquestra o smoke-test
            // detecta a falha pelo resultado do processo.
        }
        finally
        {
            allowClose = true;
            trayIcon.Hide();
            Close();
        }
    }
}
