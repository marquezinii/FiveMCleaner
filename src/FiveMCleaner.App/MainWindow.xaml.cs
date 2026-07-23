using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FiveMCleaner.App.Services;
using FiveMCleaner.App.ViewModels;
using FiveMCleaner.App.Views;
using FiveMCleaner.Contracts;

namespace FiveMCleaner.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly ThemeManager themeManager;
    private readonly TrayIconService trayIcon;
    private readonly GitHubReleaseUpdateService? releaseUpdateService;
    private readonly bool startupLaunch;
    private bool allowClose;
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
        var demoMode = syntheticDemo || commandLine
            .Any(value => value.Equals("--demo", StringComparison.OrdinalIgnoreCase));
        startupLaunch = commandLine
            .Any(value => value.Equals("--startup", StringComparison.OrdinalIgnoreCase));
        IStartupRegistrationService startupRegistration = demoMode
            ? new SessionStartupRegistrationService()
            : new WindowsStartupRegistrationService();
        releaseUpdateService = demoMode ? null : new GitHubReleaseUpdateService();
        viewModel = new MainViewModel(
            new AppOptimizationService(demoMode, syntheticDemo),
            localization: LocalizationService.Current,
            startupRegistration: startupRegistration,
            releaseUpdateService: releaseUpdateService);
        trayIcon = new TrayIconService(LocalizationService.Current);
        trayIcon.ShowRequested += TrayIcon_ShowRequested;
        trayIcon.ExitRequested += TrayIcon_ExitRequested;
        DataContext = viewModel;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        StateChanged += MainWindow_StateChanged;
        System.Windows.Application.Current.SessionEnding += Application_SessionEnding;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await viewModel.InitializeAsync();
        themeManager.Apply(viewModel.ThemePreference);
        if (startupLaunch && viewModel.MinimizeToTrayOnClose)
        {
            HideToTray();
        }
        await CaptureIfRequestedAsync();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeGlyph.Text = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = LocalizationService.Current.GetString(
            maximized ? "Window.Restore" : "Window.Maximize");
    }

    private void DashboardNav_Click(object sender, RoutedEventArgs e) => Navigate(DashboardPage, DashboardNav);

    private void OptimizerNav_Click(object sender, RoutedEventArgs e) => Navigate(OptimizerPage, OptimizerNav);

    private void HistoryNav_Click(object sender, RoutedEventArgs e) => Navigate(HistoryPage, HistoryNav);

    private void SettingsNav_Click(object sender, RoutedEventArgs e) => Navigate(SettingsPage, SettingsNav);

    private void ReviewPlan_Click(object sender, RoutedEventArgs e) => Navigate(OptimizerPage, OptimizerNav);

    private void Navigate(UIElement page, FrameworkElement navigation)
    {
        DashboardPage.Visibility = Visibility.Collapsed;
        OptimizerPage.Visibility = Visibility.Collapsed;
        HistoryPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        DashboardNav.Tag = null;
        OptimizerNav.Tag = null;
        HistoryNav.Tag = null;
        SettingsNav.Tag = null;
        page.Visibility = Visibility.Visible;
        navigation.Tag = "Selected";
    }

    private void LightProfile_Checked(object sender, RoutedEventArgs e) => viewModel.SelectProfile(OptimizationProfile.Light);

    private void BalancedProfile_Checked(object sender, RoutedEventArgs e) => viewModel.SelectProfile(OptimizationProfile.Balanced);

    private void AggressiveProfile_Checked(object sender, RoutedEventArgs e) => viewModel.SelectProfile(OptimizationProfile.Aggressive);

    private void SystemTheme_Checked(object sender, RoutedEventArgs e) => ApplyTheme(AppThemePreference.System);

    private void DarkTheme_Checked(object sender, RoutedEventArgs e) => ApplyTheme(AppThemePreference.Dark);

    private void LightTheme_Checked(object sender, RoutedEventArgs e) => ApplyTheme(AppThemePreference.Light);

    private void EnglishLanguage_Checked(object sender, RoutedEventArgs e) => ApplyLanguage(AppLanguage.English);

    private void PortugueseLanguage_Checked(object sender, RoutedEventArgs e) => ApplyLanguage(AppLanguage.PortugueseBrazil);

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
            MainWindow_StateChanged(this, EventArgs.Empty);
        }
    }

    private async void RefreshDiagnostic_Click(object sender, RoutedEventArgs e) => await viewModel.RefreshDiagnosticAsync();

    private async void StartOptimization_Click(object sender, RoutedEventArgs e)
    {
        Navigate(OptimizerPage, OptimizerNav);
        await viewModel.StartOptimizationAsync();
    }

    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        var downloaded = await viewModel.DownloadAvailableUpdateAsync();
        if (downloaded is null || !File.Exists(downloaded.InstallerPath))
        {
            return;
        }

        var decision = System.Windows.MessageBox.Show(
            LocalizationService.Current.Format(
                "Dialog.UpdateInstall.Message",
                downloaded.Version.CoreVersion),
            LocalizationService.Current.GetString("Dialog.UpdateInstall.Title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = downloaded.InstallerPath,
                UseShellExecute = true
            });
            allowClose = true;
            trayIcon.Hide();
            Close();
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            System.Windows.MessageBox.Show(
                LocalizationService.Current.Format(
                    "Dialog.UpdateInstall.Failed",
                    exception.Message),
                LocalizationService.Current.GetString("Dialog.UpdateInstall.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OpenReleaseNotes_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.ReleaseNotesUri is not { } releaseNotesUri)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = releaseNotesUri.AbsoluteUri,
            UseShellExecute = true
        });
    }

    private void CancelOptimization_Click(object sender, RoutedEventArgs e) => viewModel.CancelOptimization();

    private void CopyTechnicalReport_Click(object sender, RoutedEventArgs e) => viewModel.CopyTechnicalReport();

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
        Directory.CreateDirectory(viewModel.LogsDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{viewModel.LogsDirectory}\"",
            UseShellExecute = true
        });
    }

    private void ReportBug_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new BugReportWindow(
            new FormSubmitBugReportService(),
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
        Process.Start(new ProcessStartInfo
        {
            FileName = ProductIdentity.RepositoryUrl,
            UseShellExecute = true
        });
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (viewModel.IsBusy && !systemSessionEnding)
        {
            e.Cancel = true;
            var decision = System.Windows.MessageBox.Show(
                LocalizationService.Current.GetString("Dialog.CancelRunning.Message"),
                LocalizationService.Current.GetString("Dialog.CancelRunning.Title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (decision == MessageBoxResult.Yes)
            {
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

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        System.Windows.Application.Current.SessionEnding -= Application_SessionEnding;
        themeManager.Dispose();
        trayIcon.Dispose();
        releaseUpdateService?.Dispose();
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
        Hide();
        trayIcon.Show(announce: !trayAnnouncementShown);
        trayAnnouncementShown = true;
    }

    private void TrayIcon_ShowRequested(object? sender, EventArgs e)
    {
        trayIcon.Hide();
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
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
        Close();
    }
}
