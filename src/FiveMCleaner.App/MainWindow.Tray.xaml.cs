using System.ComponentModel;
using System.Windows;

namespace FiveMCleaner.App;

public partial class MainWindow
{
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
}
