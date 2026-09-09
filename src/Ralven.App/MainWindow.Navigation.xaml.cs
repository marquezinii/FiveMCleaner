using System.Diagnostics;
using System.Windows;
using Ralven.App.Services;
using Ralven.App.ViewModels;
using Ralven.App.Views;
using Ralven.App.Views.Pages;
using Ralven.Contracts;
using Ralven.Windows.Infrastructure;

namespace Ralven.App;

public partial class MainWindow
{
    // Keep the dashboard as the only page created during InitializeComponent. Every other page is created only when first selected.
    private SystemPage SystemPage => systemPage ??= CreateDeferredPage<SystemPage>();
    private ApplicationsPage ApplicationsPage => applicationsPage ??= CreateApplicationsPage();
    private GamesPage GamesPage => gamesPage ??= CreateDeferredPage<GamesPage>();
    private FiveMPage FiveMPage => fiveMPage ??= CreateDeferredPage<FiveMPage>();
    private OptimizerPage OptimizerPage => optimizerPage ??= CreateDeferredPage<OptimizerPage>();
    private HistoryPage HistoryPage => historyPage ??= CreateDeferredPage<HistoryPage>();
    private RalvenAiPage RalvenAiPage => ralvenAiPage ??= CreateRalvenAiPage();

    private RalvenAiPage CreateRalvenAiPage()
    {
        var page = new RalvenAiPage(
            ralvenAiService,
            cancellationToken => accountService?.GetIdTokenAsync(cancellationToken)
                ?? Task.FromResult<string?>(null),
            demoMode)
        {
            Visibility = Visibility.Collapsed
        };
        PageContentHost.Children.Add(page);
        return page;
    }

    private ApplicationsPage CreateApplicationsPage()
    {
        IWindowsApplicationInventoryInspector inspector = demoMode
            ? new SyntheticWindowsApplicationInventoryInspector()
            : new WindowsApplicationInventoryInspector();
        IWindowsApplicationPackageService packageService = demoMode
            ? new SyntheticWindowsApplicationPackageService()
            : new WinGetApplicationPackageService();
        IApplicationUpdateIgnoreStore ignoreStore = demoMode
            ? new InMemoryApplicationUpdateIgnoreStore()
            : new JsonApplicationUpdateIgnoreStore();
        var page = new ApplicationsPage(inspector, packageService, ignoreStore)
        {
            Visibility = Visibility.Collapsed
        };
        PageContentHost.Children.Add(page);
        return page;
    }

    private T CreateDeferredPage<T>() where T : UIElement, new()
    {
        var page = new T { Visibility = Visibility.Collapsed };
        PageContentHost.Children.Add(page);
        return page;
    }

    private void NavItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.NavigationViewItem { Tag: string tag } item)
        {
            return;
        }

        if (tag == "Optimizer")
        {
            RequestNavigateToOptimizer(OptimizationScope.GeneralWindows);
            return;
        }

        if (tag == "Pro")
        {
            RequestNavigateToPro();
            return;
        }

        ActivateNavItem(item);
        Navigate(tag switch
        {
            "System" => SystemPage,
            "RalvenAi" => RalvenAiPage,
            "Applications" => ApplicationsPage,
            "Games" => GamesPage,
            "History" => HistoryPage,
            "Settings" => SettingsPage,
            _ => DashboardPage
        });
    }

    private void ActivateNavItem(Wpf.Ui.Controls.NavigationViewItem selected)
    {
        DashboardNav.IsActive = ReferenceEquals(selected, DashboardNav);
        RalvenAiNav.IsActive = ReferenceEquals(selected, RalvenAiNav);
        OptimizerNav.IsActive = ReferenceEquals(selected, OptimizerNav);
        SystemNav.IsActive = ReferenceEquals(selected, SystemNav);
        ApplicationsNav.IsActive = ReferenceEquals(selected, ApplicationsNav);
        GamesNav.IsActive = ReferenceEquals(selected, GamesNav);
        HistoryNav.IsActive = ReferenceEquals(selected, HistoryNav);
        SettingsNav.IsActive = ReferenceEquals(selected, SettingsNav);
        ProNav.IsActive = ReferenceEquals(selected, ProNav);
    }

    private void Navigate(UIElement page)
    {
        DashboardPage.Visibility = Visibility.Collapsed;
        if (systemPage is not null)
        {
            systemPage.Visibility = Visibility.Collapsed;
        }
        if (applicationsPage is not null)
        {
            applicationsPage.Visibility = Visibility.Collapsed;
        }
        if (gamesPage is not null)
        {
            gamesPage.Visibility = Visibility.Collapsed;
        }
        if (fiveMPage is not null)
        {
            fiveMPage.Visibility = Visibility.Collapsed;
        }
        if (optimizerPage is not null)
        {
            optimizerPage.Visibility = Visibility.Collapsed;
        }
        if (historyPage is not null)
        {
            historyPage.Visibility = Visibility.Collapsed;
        }
        if (ralvenAiPage is not null)
        {
            ralvenAiPage.Visibility = Visibility.Collapsed;
        }
        SettingsPage.Visibility = Visibility.Collapsed;
        if (proPage is not null) proPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        RefreshLiveMetricsActivity();
    }

    // ===================== Pontes para as páginas extraídas =====================
    // As páginas em Views/Pages têm o mesmo DataContext (MainViewModel) e
    // chamam seus métodos diretamente para tudo que não depende de estado da
    // janela. Só as ações abaixo — que fecham o app, mostram diálogos de
    // confirmação nativos ou cruzam para outra página — precisam voltar para
    // o shell, que continua sendo o único dono desse estado.

    internal void RequestNavigateToGames()
    {
        ActivateNavItem(GamesNav);
        Navigate(GamesPage);
    }

    internal void RequestNavigateToFiveM()
    {
        ActivateNavItem(GamesNav);
        Navigate(FiveMPage);
    }

    internal void RequestNavigateToOptimizer(OptimizationScope scope = OptimizationScope.GeneralWindows)
    {
        viewModel.SetOptimizationScope(scope);
        ActivateNavItem(viewModel.OptimizationScope == OptimizationScope.FiveMLegacy ? GamesNav : OptimizerNav);
        Navigate(OptimizerPage);
    }

    internal void RequestNavigateToOptimizerReport()
    {
        ActivateNavItem(viewModel.OptimizationScope == OptimizationScope.FiveMLegacy ? GamesNav : OptimizerNav);
        Navigate(OptimizerPage);
    }

    internal void RequestNavigateToHistory()
    {
        ActivateNavItem(HistoryNav);
        Navigate(HistoryPage);
    }

    internal void RequestReviewRalvenAiPlan(OptimizationProfile profile)
    {
        viewModel.SetOptimizationScope(OptimizationScope.GeneralWindows);
        viewModel.SelectProfile(profile);
        ActivateNavItem(OptimizerNav);
        Navigate(OptimizerPage);
    }

    internal async Task RequestStartOptimizationAsync()
    {
        ActivateNavItem(viewModel.OptimizationScope == OptimizationScope.FiveMLegacy ? GamesNav : OptimizerNav);
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
    /// Ralven, so this window closes immediately to let it replace the
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
    /// DispatcherUnhandledException, which shuts Ralven down — closing
    /// the app over a failed "open this link" is never the right outcome.
    /// </summary>
    private static void TryOpenExternal(Action launch) => ExternalLauncher.TryOpen(launch);

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
}
