using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ralven.App.Services;
using Ralven.App.ViewModels;
using Ralven.App.Views;
using Ralven.App.Views.Pages;
using Ralven.Contracts;
using Ralven.UpdateRuntime;
using Ralven.Windows.Infrastructure;

namespace Ralven.App;

/// <summary>
/// O shell: title bar, navegação lateral e as seções de nível
/// superior. É o único dono de estado de janela (fechar, bandeja, conta,
/// atualização) — as páginas em <c>Views/Pages</c> chamam de volta os
/// métodos <c>Request*</c> públicos abaixo quando uma ação delas precisa
/// desse estado; o resto é local a cada página.
/// </summary>
public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{

    private readonly MainViewModel viewModel;
    private readonly ThemeManager themeManager;
    private readonly TrayIconService trayIcon;
    private readonly IReleaseUpdateService? releaseUpdateService;
    private readonly bool startupLaunch;
    private readonly bool demoMode;
    private readonly RemoteServicesOptions remoteServicesOptions;
    private readonly QueuedCloudflareTelemetryService? queuedCloudflareTelemetry;
    private SystemPage? systemPage;
    private ApplicationsPage? applicationsPage;
    private GamesPage? gamesPage;
    private OptimizerPage? optimizerPage;
    private HistoryPage? historyPage;
    private readonly IFirebaseAuthService? accountService;
    private readonly IAccountProfileService profileService;
    private readonly CloudflareAccountEntitlementService? entitlementService;
    private readonly IGoogleOAuthClient googleOAuth;
    private HwndSource? windowSource;
    private bool allowClose;
    private bool closeAfterOptimizationStops;
    private bool trayAnnouncementShown;
    private bool systemSessionEnding;
    private bool syncingLanguageSelector;
    private bool crashReportingConfigured;
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

        if (TryCreateHttpsEndpoint(remoteServicesOptions.AccountProfileEndpoint, out var profileEndpoint))
        {
            profileService = new CloudflareAccountProfileService(profileEndpoint);
            entitlementService = new CloudflareAccountEntitlementService(profileEndpoint);
        }
        else
        {
            profileService = new DisabledAccountProfileService();
            entitlementService = null;
        }

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

        accountService = CreateAccountService(demoMode, remoteServicesOptions, profileService);
        if (accountService is not null)
        {
            accountService.StateChanged += AccountService_StateChanged;
        }

        // Plan.Title and the Refresh button follow the language automatically
        // through their {Binding [key], Source={StaticResource
        // LocalizedStrings}} markup, but the entitlement value/detail text is
        // set imperatively (it depends on server state, not just a static
        // key), so it needs its own re-render on language change.
        LocalizationService.Current.LanguageChanged += MainWindow_LanguageChanged;

        var telemetry = CreateTelemetryServices(demoMode, remoteServicesOptions, runtimeEnvironment);
        queuedCloudflareTelemetry = telemetry.Queued;

        viewModel = new MainViewModel(
            new AppOptimizationService(demoMode, commandLine.SyntheticDemo) { AuthorizePro = AuthorizeProOperationAsync },
            localization: LocalizationService.Current,
            startupRegistration: startupRegistration,
            releaseUpdateService: releaseUpdateService,
            telemetry: telemetry.Service,
            silentUpdateInstaller: silentUpdateInstaller,
            liveAlertService: liveAlertService,
            windowsGamingControls: new WindowsGamingControlsService(demoMode),
            windowsSystemHealthInspector: demoMode
                ? new SyntheticWindowsSystemHealthInspector()
                : new WindowsSystemHealthInspector(),
            personalWorkspaceService: new PersonalWorkspaceService(AuthorizeProOperationAsync, inMemory: demoMode));

        // StateChanged only fires once RestoreSessionAsync actually finds a
        // stored session; a fresh install or an already-signed-out user
        // never raises it, so the Settings card needs one explicit call here
        // to land on the right panel (unavailable/signed-out/signed-in)
        // instead of relying on whatever Visibility happens to be XAML's
        // default.
        RefreshAccountSettingsCard();
        if (!string.IsNullOrWhiteSpace(commandLine.JustUpdatedVersion))
        {
            viewModel.ReportCompletedUpdate(commandLine.JustUpdatedVersion);
        }
        trayIcon = new TrayIconService(LocalizationService.Current);
        trayIcon.ShowRequested += TrayIcon_ShowRequested;
        trayIcon.ExitRequested += TrayIcon_ExitRequested;
        viewModel.UpdateAvailableDetected += ViewModel_UpdateAvailableDetected;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        DataContext = viewModel;
        Loaded += MainWindow_Loaded;
        SizeChanged += MainWindow_SizeChanged;
        SourceInitialized += MainWindow_SourceInitialized;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        System.Windows.Application.Current.SessionEnding += Application_SessionEnding;
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width < 1200)
        {
            RootNavigationView.IsPaneOpen = false;
        }
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
                Path.Combine(installRoot!, "Ralven.Launcher.exe"));
    }

    private IReleaseUpdateService? CreateReleaseUpdateService(bool demoMode, string? runtimeRoot)
    {
        return demoMode
            ? null
            : new SignedManifestUpdateService(
                runtimeRoot is null ? ReleasePackageKind.Installer : ReleasePackageKind.Runtime);
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
                Path.Combine(installRoot!, "Ralven.Launcher.exe"));
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
            Path.Combine(AppContext.BaseDirectory, "updater", "Ralven.Updater.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ProductIdentity.Name,
                "Updater"));
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
            LanguageSelector.SelectedIndex = viewModel.LanguagePreference switch
            {
                AppLanguagePreference.PortugueseBrazil => 1,
                AppLanguagePreference.English => 2,
                AppLanguagePreference.Spanish => 3,
                _ => 0
            };
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
        if (startupLaunch && viewModel.StartMinimized)
        {
            HideToTray();
        }
        await CaptureIfRequestedAsync();
    }

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

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        applicationsPage?.Dispose();
        viewModel.Dispose();
        windowSource?.RemoveHook(WindowMessageHook);
        System.Windows.Application.Current.SessionEnding -= Application_SessionEnding;
        viewModel.UpdateAvailableDetected -= ViewModel_UpdateAvailableDetected;
        viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        LocalizationService.Current.LanguageChanged -= MainWindow_LanguageChanged;
        themeManager.Dispose();
        trayIcon.Dispose();
        CancelAccountEntitlementExpiry();
        accountService?.Dispose();
        (releaseUpdateService as IDisposable)?.Dispose();
    }

    private void MainWindow_LanguageChanged(object? sender, AppLanguageChangedEventArgs e) =>
        ApplyAccountEntitlementPresentation();

    private void Application_SessionEnding(object? sender, SessionEndingCancelEventArgs e)
    {
        // Nunca transforma a preferência de bandeja em bloqueio de logoff/desligamento.
        systemSessionEnding = true;
        allowClose = true;
        viewModel.CancelOptimization();
    }
}
