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
using FiveMCleaner.App.Views.Pages;
using FiveMCleaner.Contracts;
using FiveMCleaner.UpdateRuntime;

namespace FiveMCleaner.App;

/// <summary>
/// O shell: title bar, navegação lateral e as quatro seções de nível
/// superior. É o único dono de estado de janela (fechar, bandeja, conta,
/// atualização) — as páginas em <c>Views/Pages</c> chamam de volta os
/// métodos <c>Request*</c> públicos abaixo quando uma ação delas precisa
/// desse estado; o resto é local a cada página.
/// </summary>
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
    private OptimizerPage? optimizerPage;
    private HistoryPage? historyPage;
    private readonly IFirebaseAuthService? accountService;
    private readonly IAccountProfileService profileService;
    private readonly IGoogleOAuthClient googleOAuth;
    private HwndSource? windowSource;
    private bool allowClose;
    private bool closeAfterOptimizationStops;
    private bool trayAnnouncementShown;
    private bool systemSessionEnding;
    private bool syncingLanguageSelector;

    // Keep the dashboard as the only non-deferred page during InitializeComponent. The optimizer and history views are heavier and are created only when first selected.
    private OptimizerPage OptimizerPage => optimizerPage ??= CreateDeferredPage<OptimizerPage>();
    private HistoryPage HistoryPage => historyPage ??= CreateDeferredPage<HistoryPage>();

    private T CreateDeferredPage<T>() where T : UIElement, new()
    {
        var page = new T { Visibility = Visibility.Collapsed };
        PageContentHost.Children.Add(page);
        return page;
    }

    public MainWindow()
    {
        InitializeComponent();
        // Precisa ser marcado em código, não em XAML: setar IsChecked="True"
        // inline dispara o evento Checked durante o próprio parse do
        // documento, antes de os outros campos nomeados existirem.
        CategoryGeneral.IsChecked = true;
        themeManager = new ThemeManager();
        themeManager.Apply(AppThemePreference.System);

        var commandLine = ParseCommandLine();
        demoMode = commandLine.DemoMode;
        startupLaunch = commandLine.StartupLaunch;

        var runtimeLayout = RuntimeLayout.Resolve(AppContext.BaseDirectory);
        var installRoot = runtimeLayout.InstallRoot;
        var runtimeRoot = runtimeLayout.RuntimeRoot;

        var startupRegistration = CreateStartupRegistrationService(demoMode, installRoot, runtimeRoot);
        releaseUpdateService = CreateReleaseUpdateService(demoMode, runtimeRoot);
        var silentUpdateInstaller = CreateSilentUpdateInstaller(demoMode, installRoot, runtimeRoot);

        var runtimeEnvironment = AppEnvironment.Resolve();
        remoteServicesOptions = RemoteServicesOptionsLoader.Load(runtimeEnvironment, AppContext.BaseDirectory);

        profileService = TryCreateHttpsEndpoint(remoteServicesOptions.AccountProfileEndpoint, out var profileEndpoint)
            ? new CloudflareAccountProfileService(profileEndpoint)
            : new DisabledAccountProfileService();

        // Demo runs never poll the live alert -- same trade as telemetry below.
        ILiveAlertService? liveAlertService = !demoMode
            && TryCreateHttpsEndpoint(remoteServicesOptions.LiveAlertEndpoint, out var liveAlertEndpoint)
                ? new CloudflareLiveAlertService(liveAlertEndpoint)
                : null;

        // Demo runs never talk to Google: an unconfigured client reports
        // IsConfigured=false and the account window hides the button.
        googleOAuth = new GoogleOAuthClient(
            demoMode ? null : remoteServicesOptions.GoogleOAuthClientId,
            demoMode ? null : remoteServicesOptions.GoogleOAuthClientSecret);

        accountService = CreateAccountService(demoMode, remoteServicesOptions);
        if (accountService is not null)
        {
            accountService.StateChanged += AccountService_StateChanged;
        }

        // StateChanged only fires once RestoreSessionAsync actually finds a
        // stored session; a fresh install or an already-signed-out user
        // never raises it, so the Settings card needs one explicit call here
        // to land on the right panel (unavailable/signed-out/signed-in)
        // instead of relying on whatever Visibility happens to be XAML's
        // default.
        RefreshAccountSettingsCard();

        var telemetry = CreateTelemetryServices(demoMode, remoteServicesOptions, runtimeEnvironment);
        queuedCloudflareTelemetry = telemetry.Queued;

        viewModel = new MainViewModel(
            new AppOptimizationService(demoMode, commandLine.SyntheticDemo),
            localization: LocalizationService.Current,
            startupRegistration: startupRegistration,
            releaseUpdateService: releaseUpdateService,
            telemetry: telemetry.Service,
            silentUpdateInstaller: silentUpdateInstaller,
            liveAlertService: liveAlertService);
        if (!string.IsNullOrWhiteSpace(commandLine.JustUpdatedVersion))
        {
            viewModel.ReportCompletedUpdate(commandLine.JustUpdatedVersion);
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

    private sealed record MainWindowCommandLine(
        bool DemoMode,
        bool SyntheticDemo,
        bool StartupLaunch,
        string? JustUpdatedVersion);

    private static MainWindowCommandLine ParseCommandLine()
    {
        var commandLine = Environment.GetCommandLineArgs();
        var syntheticDemo = commandLine
            .Any(value => value.Equals("--demo-synthetic", StringComparison.OrdinalIgnoreCase));
        var demoMode = syntheticDemo || commandLine
            .Any(value => value.Equals("--demo", StringComparison.OrdinalIgnoreCase));
        var startupLaunch = commandLine
            .Any(value => value.Equals("--startup", StringComparison.OrdinalIgnoreCase));
        var justUpdatedVersion = commandLine
            .FirstOrDefault(value => value.StartsWith("--updated=", StringComparison.OrdinalIgnoreCase))
            ?["--updated=".Length..];
        return new MainWindowCommandLine(demoMode, syntheticDemo, startupLaunch, justUpdatedVersion);
    }

    private IStartupRegistrationService CreateStartupRegistrationService(
        bool demoMode,
        string? installRoot,
        string? runtimeRoot)
    {
        if (demoMode)
        {
            return new SessionStartupRegistrationService();
        }

        return runtimeRoot is null
            ? new WindowsStartupRegistrationService()
            : new WindowsStartupRegistrationService(
                Path.Combine(installRoot!, "FiveMCleaner.Launcher.exe"));
    }

    private IReleaseUpdateService? CreateReleaseUpdateService(bool demoMode, string? runtimeRoot)
    {
        return demoMode
            ? null
            : runtimeRoot is null ? new GitHubReleaseUpdateService() : new SignedManifestUpdateService();
    }

    private ISilentUpdateInstaller? CreateSilentUpdateInstaller(
        bool demoMode,
        string? installRoot,
        string? runtimeRoot)
    {
        if (demoMode)
        {
            return null;
        }

        if (runtimeRoot is not null)
        {
            return new AtomicUpdateInstaller(
                runtimeRoot,
                Path.Combine(installRoot!, "FiveMCleaner.Launcher.exe"));
        }

        return new SilentUpdateInstaller(
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
    }

    private IFirebaseAuthService? CreateAccountService(
        bool demoMode,
        RemoteServicesOptions options)
    {
        if (demoMode
            || !FirebaseAuthConfiguration.TryGetApiKey(options.FirebaseApiKey, out var firebaseApiKey))
        {
            return null;
        }

        var service = new FirebaseAuthService(firebaseApiKey);
        service.StateChanged += (_, _) => Dispatcher.Invoke(UpdateAccountButton);
        return service;
    }

    private sealed record MainWindowTelemetry(
        IAnonymousTelemetryService Service,
        QueuedCloudflareTelemetryService? Queued);

    /// <summary>
    /// Creates the telemetry services based on the configured endpoint.
    /// If the endpoint is missing or malformed, telemetry safely does
    /// nothing rather than crash.
    /// </summary>
    private MainWindowTelemetry CreateTelemetryServices(
        bool demoMode,
        RemoteServicesOptions options,
        AppRuntimeEnvironment runtimeEnvironment)
    {
        if (demoMode)
        {
            return new MainWindowTelemetry(DisabledAnonymousTelemetryService.Instance, null);
        }

        if (TelemetryEndpointPolicy.TryCreate(
            options.TelemetryEndpoint,
            runtimeEnvironment,
            out var telemetryEndpoint,
            out _))
        {
            var queued = new QueuedCloudflareTelemetryService(
                new LocalTelemetryQueue(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    ProductIdentity.Name,
                    "Telemetry",
                    "pending")),
                new CloudflareTelemetryTransport(telemetryEndpoint, options.Environment));
            return new MainWindowTelemetry(queued, queued);
        }

        return new MainWindowTelemetry(DisabledAnonymousTelemetryService.Instance, null);
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

        try
        {
            await viewModel.InitializeAsync();
        }
        catch
        {
            // O recibo já foi confirmado acima (por desenho, antes da
            // inicialização terminar). Se a própria inicialização falhar
            // logo em seguida, invalidar o recibo garante que o launcher
            // ainda enxergue esta versão como não confirmada e possa
            // reverter dentro da janela de saúde, em vez de confiar num
            // recibo escrito antes da falha.
            if (!demoMode)
            {
                InvalidateUpdateHealthReceiptIfRequested();
            }

            throw;
        }
        if (accountService is not null)
        {
            _ = RestoreAccountSessionQuietlyAsync();
        }
        themeManager.Apply(viewModel.ThemePreference);
        // A sincronização programática do seletor não pode acionar o
        // SelectionChanged: ele converteria uma preferência "Automatic" em
        // um idioma fixo (o detectado), gravando o pin no primeiro launch.
        syncingLanguageSelector = true;
        try
        {
            LanguageSelector.SelectedIndex = viewModel.IsPortugueseSelected
                ? 0
                : viewModel.IsSpanishSelected ? 2 : 1;
        }
        finally
        {
            syncingLanguageSelector = false;
        }
        switch (viewModel.ThemePreference)
        {
            case AppThemePreference.Dark:
                ThemeDarkOption.IsChecked = true;
                break;
            case AppThemePreference.Light:
                ThemeLightOption.IsChecked = true;
                break;
            default:
                ThemeSystemOption.IsChecked = true;
                break;
        }
        if (!demoMode)
        {
            await ShowPrivacyConsentIfNeededAsync();
            await ShowReleaseNotesIfNeededAsync();
            InitializeCrashReportingIfAuthorized();
            await FlushPendingTelemetryIfAnyAsync();
        }
        if (startupLaunch && viewModel.MinimizeToTrayOnClose)
        {
            HideToTray();
        }
        await CaptureIfRequestedAsync();
    }

    private async Task RestoreAccountSessionQuietlyAsync()
    {
        try
        {
            await accountService!.RestoreSessionAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
        }
    }

    private void Account_Click(object sender, RoutedEventArgs e)
    {
        if (accountService is null)
        {
            System.Windows.MessageBox.Show(LocalizationService.Current.GetString("Settings.Account.Unavailable"), LocalizationService.Current.GetString("Settings.Account.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (accountService.Current.State == AuthenticationState.SignedIn)
        {
            // Já logado: o clique no cabeçalho leva direto para
            // Configurações > Sua conta, em vez de reabrir a janela de
            // entrar/cadastrar (que agora só cuida de autenticar, não de
            // gerenciar a conta -- ver AccountWindow.CloseAfterSignIn).
            ActivateNavItem(SettingsNav);
            Navigate(SettingsPage);
            AccountSettingsCard.BringIntoView();
            return;
        }

        OpenAccountWindow();
    }

    private void OpenAccountWindow()
    {
        var dialog = new AccountWindow(accountService!, profileService, googleOAuth) { Owner = this };
        if (dialog.ShowDialog() == true) UpdateAccountButton();
    }

    private void UpdateAccountButton()
    {
        var profile = accountService?.Current.User;
        AccountLabel.Text = profile?.DisplayName ?? LocalizationService.Current.GetString("Account.SignInButton");
        AccountButton.ToolTip = profile is null ? LocalizationService.Current.GetString("Account.SignInTooltip") : LocalizationService.Current.GetString("Account.ViewTooltip");
        // Also sets AccountInitials/avatar for both the header and the
        // Settings card, so a direct assignment here would just be
        // immediately overwritten.
        RefreshAccountSettingsCard();
    }

    private async void AccountService_StateChanged(object? sender, AuthenticationSnapshot snapshot)
    {
        Dispatcher.Invoke(UpdateAccountButton);
        if (snapshot.State != AuthenticationState.SignedIn || snapshot.User is null)
        {
            Dispatcher.Invoke(() =>
            {
                viewModel.SetAccountFirstName(null);
                ApplyAccountSettingsUsername(null);
            });
            return;
        }

        await SyncAccountFirstNameAsync();
    }

    /// <summary>
    /// Reads the caller's own first name for the Overview greeting. Firebase
    /// Authentication REST never stores it, so it only exists in the
    /// Worker's profile table; this is why login and quiet session restore
    /// both need a read call instead of getting it for free off the token.
    /// </summary>
    private async Task SyncAccountFirstNameAsync()
    {
        if (accountService is null)
        {
            return;
        }

        try
        {
            var idToken = await accountService.GetIdTokenAsync().ConfigureAwait(false);
            if (idToken is null)
            {
                return;
            }

            var result = await profileService.FetchAsync(idToken).ConfigureAwait(false);
            if (result.Outcome == AccountProfileFetchOutcome.Found)
            {
                Dispatcher.Invoke(() =>
                {
                    viewModel.SetAccountFirstName(result.FirstName);
                    ApplyAccountSettingsUsername(result.Username);
                });
            }
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            // Sem nome não é um estado de erro visível: a saudação simplesmente
            // fica sem o nome até a próxima sincronização bem-sucedida.
        }
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

    private static void InvalidateUpdateHealthReceiptIfRequested()
    {
        var runtimeRoot = RuntimeLayout.Resolve(AppContext.BaseDirectory).RuntimeRoot;
        if (runtimeRoot is null) return;
        try { new UpdateHealthReceiptStore(runtimeRoot).Invalidate(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            // Melhor esforço: se não for possível invalidar aqui, a
            // reverificação do launcher na próxima abertura é o fallback.
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
            viewModel.ShareAnonymousTelemetry)
        {
            Owner = this
        };
        consentWindow.ShowDialog();
        await viewModel.ConfirmPrivacyConsentAsync(consentWindow.AcceptedAnonymousTelemetry);
    }

    /// <summary>
    /// Shows the informational, non-blocking "What's New" panel when
    /// <see cref="MainViewModel.PendingReleaseNotes"/> (computed once, right
    /// after settings finish loading in
    /// <see cref="MainViewModel.InitializeAsync"/>) says this version has
    /// notes the user has not seen yet. Persistence only happens after the
    /// panel is actually closed, so a crash before that point leaves the
    /// notes unseen and they are shown again next launch. When there is
    /// nothing to show but the evaluator still wants the current version
    /// recorded as a baseline (brand-new installation, or a version with no
    /// catalog entry), that happens immediately instead. Demo mode never
    /// shows this screen, for the same reason it never shows the privacy
    /// consent screen: it never persists settings, and smoke tests must not
    /// hang on a modal.
    /// </summary>
    private async Task ShowReleaseNotesIfNeededAsync()
    {
        var decision = viewModel.PendingReleaseNotes;
        if (decision is null)
        {
            return;
        }

        if (decision.ShouldShow && decision.Entry is not null)
        {
            var releaseNotesWindow = new ReleaseNotesWindow(decision.Entry) { Owner = this };
            releaseNotesWindow.ShowDialog();
            await viewModel.ConfirmReleaseNotesSeenAsync(decision.Entry.Version);
            return;
        }

        if (decision.ShouldRecordSilently)
        {
            await viewModel.ConfirmReleaseNotesSeenAsync(viewModel.AppVersion);
        }
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
        CrashReporting.Current = new SentryCrashReportingService();
        CrashReporting.Current.Initialize(remoteServicesOptions, viewModel.AppVersion);
    }

    /// <summary>
    /// Retries sending whatever telemetry could not be delivered during a
    /// previous, possibly offline, run. A no-op unless the Cloudflare
    /// transport is the active one (<see cref="queuedCloudflareTelemetry"/>
    /// is only set when a telemetry endpoint is configured).
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

    private void Navigate(UIElement page)
    {
        DashboardPage.Visibility = Visibility.Collapsed;
        if (optimizerPage is not null)
        {
            optimizerPage.Visibility = Visibility.Collapsed;
        }
        if (historyPage is not null)
        {
            historyPage.Visibility = Visibility.Collapsed;
        }
        SettingsPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        viewModel.SetLiveMetricsEnabled(ReferenceEquals(page, DashboardPage));
    }

    // ===================== Pontes para as páginas extraídas =====================
    // As páginas em Views/Pages têm o mesmo DataContext (MainViewModel) e
    // chamam seus métodos diretamente para tudo que não depende de estado da
    // janela. Só as ações abaixo — que fecham o app, mostram diálogos de
    // confirmação nativos ou cruzam para outra página — precisam voltar para
    // o shell, que continua sendo o único dono desse estado.

    internal void RequestNavigateToOptimizer()
    {
        ActivateNavItem(OptimizerNav);
        Navigate(OptimizerPage);
    }

    internal void RequestNavigateToHistory()
    {
        ActivateNavItem(HistoryNav);
        Navigate(HistoryPage);
    }

    internal async Task RequestStartOptimizationAsync()
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

    internal void RequestCancelOptimization()
    {
        if (!viewModel.IsBusy || !ConfirmOptimizationInterruption(closeApplication: false))
        {
            return;
        }

        viewModel.CancelOptimization();
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
    internal async Task RequestDownloadUpdateAsync()
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

    internal void RequestOpenReleaseNotes()
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

    /// <summary>
    /// Shell launches fail for reasons outside the app's control (no default
    /// browser or folder handler registered, group policy blocking the verb,
    /// denied access to the folder). Unhandled, they reach
    /// DispatcherUnhandledException, which shuts FiveMCleaner down — closing
    /// the app over a failed "open this link" is never the right outcome.
    /// </summary>
    private static void TryOpenExternal(Action launch) => ExternalLauncher.TryOpen(launch);

    /// <summary>
    /// Duas regiões de Configurações: a categoria marcada decide qual painel
    /// de conteúdo aparece. Só um fica visível por vez; nenhuma outra lógica
    /// de navegação — a rolagem e a categoria são independentes da página
    /// selecionada na barra lateral.
    /// </summary>
    private void SettingsCategory_Changed(object sender, RoutedEventArgs e)
    {
        AccountSettingsCard.Visibility = ReferenceEquals(sender, CategoryAccount) ? Visibility.Visible : Visibility.Collapsed;
        GeneralSettingsPanel.Visibility = ReferenceEquals(sender, CategoryGeneral) ? Visibility.Visible : Visibility.Collapsed;
        PrivacySettingsPanel.Visibility = ReferenceEquals(sender, CategoryPrivacy) ? Visibility.Visible : Visibility.Collapsed;
        ToolsSettingsPanel.Visibility = ReferenceEquals(sender, CategoryTools) ? Visibility.Visible : Visibility.Collapsed;
        AboutSettingsPanel.Visibility = ReferenceEquals(sender, CategoryAbout) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SystemTheme_Checked(object sender, RoutedEventArgs e) => ApplyTheme(AppThemePreference.System);

    private void DarkTheme_Checked(object sender, RoutedEventArgs e) => ApplyTheme(AppThemePreference.Dark);

    private void LightTheme_Checked(object sender, RoutedEventArgs e) => ApplyTheme(AppThemePreference.Light);

    private void LanguageSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (syncingLanguageSelector || !IsLoaded || LanguageSelector.SelectedItem is not System.Windows.Controls.ComboBoxItem item)
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

    private async void RunGtaVBenchmark_Click(object sender, RoutedEventArgs e) => await viewModel.RunGtaVBenchmarkAsync();

    private async void CheckForUpdatesManually_Click(object sender, RoutedEventArgs e) => await viewModel.CheckForUpdatesManuallyAsync();

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

    private void Discord_Click(object sender, RoutedEventArgs e)
    {
        TryOpenExternal(() => Process.Start(new ProcessStartInfo
        {
            FileName = ProductIdentity.DiscordInviteUrl,
            UseShellExecute = true
        }));
    }

    private void LiveAlertDismiss_Click(object sender, RoutedEventArgs e) => viewModel.DismissLiveAlert();

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
        accountService?.Dispose();
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

    private void TrayIcon_ShowRequested(object? sender, EventArgs e) => RequestActivation();

    /// <summary>
    /// Brings the main window back to the foreground: reveals it if it was
    /// hidden to the tray, restores it maximized if it was minimized, and
    /// activates it. Reused by the tray (open/double-click/notification) and
    /// by the single-instance activation request raised when the user opens
    /// the app while it is already running.
    /// </summary>
    public void RequestActivation()
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

            // O modo demo devolve AppSettings padrão de propósito (nunca lê
            // nem grava o arquivo do usuário), então o tema capturado sempre
            // seguia o do Windows — o tema claro era, na prática, impossível
            // de fotografar sem trocar a preferência do sistema inteiro da
            // pessoa. --capture-theme= resolve isso pelo mesmo caminho que
            // --capture-page= já usa para as páginas: só vale sob --capture=,
            // não persiste nada, e não existe fora do smoke-test.
            var theme = Environment.GetCommandLineArgs()
                .FirstOrDefault(value => value.StartsWith("--capture-theme=", StringComparison.OrdinalIgnoreCase));
            if (theme is not null)
            {
                var requested = theme["--capture-theme=".Length..].Trim('"');
                themeManager.Apply(requested.Equals("light", StringComparison.OrdinalIgnoreCase)
                    ? AppThemePreference.Light
                    : AppThemePreference.Dark);
            }

            // O smoke-test de captura sempre abriu na Visão geral. Com
            // --capture-page= ele consegue fotografar qualquer página, o que
            // é o único jeito de conferir o Otimizador sem interação manual.
            var page = Environment.GetCommandLineArgs()
                .FirstOrDefault(value => value.StartsWith("--capture-page=", StringComparison.OrdinalIgnoreCase));
            if (page is not null)
            {
                var tag = page["--capture-page=".Length..].Trim('"');
                var target = tag switch
                {
                    "Optimizer" => (Element: (UIElement)OptimizerPage, Nav: OptimizerNav),
                    "History" => (HistoryPage, HistoryNav),
                    "Settings" => (SettingsPage, SettingsNav),
                    _ => (DashboardPage, DashboardNav)
                };
                ActivateNavItem(target.Nav);
                Navigate(target.Element);
            }

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
