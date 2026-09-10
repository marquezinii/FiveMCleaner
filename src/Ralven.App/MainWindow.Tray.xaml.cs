using System.ComponentModel;
using System.Windows;

namespace Ralven.App;

public partial class MainWindow
{
    private void LiveAlertDismiss_Click(object sender, RoutedEventArgs e) => viewModel.DismissLiveAlert();

    private void MainWindow_ActivityChanged(object? sender, EventArgs e) => RefreshLiveMetricsActivity();

    private void RefreshLiveMetricsActivity() => viewModel.SetLiveMetricsEnabled(
        startupCompleted && IsVisible && IsActive && WindowState != WindowState.Minimized
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

    /// <summary>
    /// Brings the main window back to the foreground: reveals it if it was
    /// hidden to the tray, restores it maximized if it was minimized, and
    /// activates it. Reused by the tray (open/double-click/notification) and
    /// by the single-instance activation request raised when the user opens
    /// the app while it is already running.
    /// </summary>
    public void RequestActivation()
    {
        activationRequested = true;
        trayIcon.Hide();
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Maximized;
        }

        Activate();
        RefreshLiveMetricsActivity();
    }

    private void TrayIcon_ExitRequested(object? sender, EventArgs e)
    {
        if (viewModel.IsWindowsGamingBusy)
        {
            RequestActivation();
            return;
        }

        allowClose = true;
        trayIcon.Hide();
        Close();
    }
}
