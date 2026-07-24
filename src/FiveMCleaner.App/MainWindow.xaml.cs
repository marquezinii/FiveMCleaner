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

namespace FiveMCleaner.App;

public partial class MainWindow : Window
{
    private const uint MonitorDefaultToNearest = 2;
    private const int WmGetMinMaxInfo = 0x0024;
    private readonly MainViewModel viewModel;
    private readonly ThemeManager themeManager;
    private readonly TrayIconService trayIcon;
    private readonly GitHubReleaseUpdateService? releaseUpdateService;
    private readonly bool startupLaunch;
    private HwndSource? windowSource;
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
            releaseUpdateService: releaseUpdateService,
            telemetry: demoMode
                ? DisabledAnonymousTelemetryService.Instance
                : new FormSubmitAnonymousTelemetryService());
        trayIcon = new TrayIconService(LocalizationService.Current);
        trayIcon.ShowRequested += TrayIcon_ShowRequested;
        trayIcon.ExitRequested += TrayIcon_ExitRequested;
        DataContext = viewModel;
        Loaded += MainWindow_Loaded;
        SourceInitialized += MainWindow_SourceInitialized;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        StateChanged += MainWindow_StateChanged;
        System.Windows.Application.Current.SessionEnding += Application_SessionEnding;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await viewModel.InitializeAsync();
        themeManager.Apply(viewModel.ThemePreference);
        LanguageSelector.SelectedIndex = viewModel.IsPortugueseSelected ? 0 : 1;
        ThemeSelector.SelectedIndex = viewModel.ThemePreference switch
        {
            AppThemePreference.Dark => 1,
            AppThemePreference.Light => 2,
            _ => 0
        };
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

    private void LanguageSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded || LanguageSelector.SelectedItem is not System.Windows.Controls.ComboBoxItem item)
        {
            return;
        }

        ApplyLanguage(string.Equals(item.Tag as string, "pt-BR", StringComparison.Ordinal)
            ? AppLanguage.PortugueseBrazil
            : AppLanguage.English);
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
        windowSource?.RemoveHook(WindowMessageHook);
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
