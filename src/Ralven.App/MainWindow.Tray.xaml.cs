using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Ralven.App.Services;
using Ralven.Contracts;

namespace Ralven.App;

public partial class MainWindow
{
    private void LiveAlertDismiss_Click(object sender, RoutedEventArgs e) => viewModel.DismissLiveAlert();

    private void MainWindow_ActivityChanged(object? sender, EventArgs e) => RefreshLiveMetricsActivity();

    private void RefreshLiveMetricsActivity() => viewModel.SetLiveMetricsEnabled(
        IsVisible && IsActive && WindowState != WindowState.Minimized
        && DashboardPage.Visibility == Visibility.Visible);

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (viewModel.IsWindowsGamingBusy && !systemSessionEnding)
        {
            e.Cancel = true;
            return;
        }

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

    private void HideToTray()
    {
        viewModel.SetLiveMetricsEnabled(false);
        Hide();
        trayIcon.Show(announce: !trayAnnouncementShown);
        trayAnnouncementShown = true;
    }

    private void ViewModel_UpdateAvailableDetected(object? sender, string version)
    {
        // The in-app update banner remains available independently; this
        // preference controls only the native Windows notification.
        if (viewModel.NotifyWhenUpdateAvailable)
        {
            trayIcon.ShowUpdateAvailable(version);
        }
    }

    private void TrayIcon_ShowRequested(object? sender, EventArgs e) => RequestActivation();

    private void TrayIcon_MenuRequested(object? sender, EventArgs e)
    {
        _ = Dispatcher.BeginInvoke(ShowTrayMenu, DispatcherPriority.Input);
    }

    private void ShowTrayMenu()
    {
        trayMenu.DataContext = viewModel;
        trayMenu.IsOpen = false;
        trayMenu.IsOpen = true;
    }

    private void TrayMenu_Opened(object sender, RoutedEventArgs e)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            trayMenu.Items
                .OfType<System.Windows.Controls.MenuItem>()
                .FirstOrDefault(item => item.Focusable && item.IsEnabled)
                ?.Focus();
        }, DispatcherPriority.Input);
    }

    private void TrayOpen_Click(object sender, RoutedEventArgs e) => RequestActivation();

    private void TrayOptimize_Click(object sender, RoutedEventArgs e)
    {
        RequestActivation();
        RequestNavigateToOptimizer(OptimizationScope.GeneralWindows);
    }

    private void TraySessionMonitor_Click(object sender, RoutedEventArgs e) =>
        viewModel.ToggleFiveMSessionMonitor();

    private async void TrayCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.AvailableUpdateVersion is not null)
        {
            RequestActivation();
            ActivateNavItem(DashboardNav);
            Navigate(DashboardPage);
            return;
        }

        await viewModel.CheckForUpdatesManuallyAsync();
        if (viewModel.AvailableUpdateVersion is null
            && viewModel.ManualUpdateCheckMessage is { Length: > 0 } message)
        {
            trayIcon.ShowInformation(
                LocalizationService.Current.GetString("Tray.UpdateCheck.Title"),
                message);
        }
    }

    private void TraySettings_Click(object sender, RoutedEventArgs e)
    {
        RequestActivation();
        ActivateNavItem(SettingsNav);
        Navigate(SettingsPage);
    }

    /// <summary>
    /// Brings the main window back to the foreground: reveals it if it was
    /// hidden to the tray, restores it maximized if it was minimized, and
    /// activates it. Reused by the tray (open/double-click/notification) and
    /// by the single-instance activation request raised when the user opens
    /// the app while it is already running.
    /// </summary>
    public void RequestActivation()
    {
        trayMenu.IsOpen = false;
        trayIcon.SetPersistentVisibility(viewModel.MinimizeToTrayOnClose);
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Maximized;
        }

        Activate();
        RefreshLiveMetricsActivity();
    }

    private void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.IsWindowsGamingBusy)
        {
            RequestActivation();
            return;
        }

        if (viewModel.IsBusy)
        {
            // The interruption dialog must have a visible owner. If the user
            // keeps the run going, both the window and tray icon stay usable.
            RequestActivation();
            Close();
            return;
        }

        allowClose = true;
        trayIcon.Hide();
        Close();
    }

    private void RefreshTrayIconPresentation()
    {
        var status = viewModel.IsBusy && !string.IsNullOrWhiteSpace(viewModel.ProgressHeadline)
            ? viewModel.ProgressHeadline
            : viewModel.IsUpdateBannerVisible && !string.IsNullOrWhiteSpace(viewModel.UpdateBannerTitle)
                ? viewModel.UpdateBannerTitle
                : viewModel.IsFiveMSessionMonitoring && !string.IsNullOrWhiteSpace(viewModel.FiveMSessionStatusLabel)
                    ? viewModel.FiveMSessionStatusLabel
                    : viewModel.IsLiveAlertIconVisible && !string.IsNullOrWhiteSpace(viewModel.LiveAlertMessage)
                        ? viewModel.LiveAlertMessage
                        : LocalizationService.Current.GetString("Tray.Status.Ready");

        trayIcon.SetPersistentVisibility(viewModel.MinimizeToTrayOnClose);
        trayIcon.UpdateToolTip(LocalizationService.Current.Format("Tray.Tooltip", status));
    }
}
