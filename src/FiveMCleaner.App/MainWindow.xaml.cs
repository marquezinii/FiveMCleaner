using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FiveMCleaner.App.Services;
using FiveMCleaner.App.ViewModels;
using FiveMCleaner.Contracts;

namespace FiveMCleaner.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;

    public MainWindow()
    {
        InitializeComponent();
        var demoMode = Environment.GetCommandLineArgs()
            .Any(value => value.Equals("--demo", StringComparison.OrdinalIgnoreCase));
        viewModel = new MainViewModel(new AppOptimizationService(demoMode));
        DataContext = viewModel;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await viewModel.InitializeAsync();
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

    private async void RefreshDiagnostic_Click(object sender, RoutedEventArgs e) => await viewModel.RefreshDiagnosticAsync();

    private async void StartOptimization_Click(object sender, RoutedEventArgs e)
    {
        Navigate(OptimizerPage, OptimizerNav);
        await viewModel.StartOptimizationAsync();
    }

    private void CancelOptimization_Click(object sender, RoutedEventArgs e) => viewModel.CancelOptimization();

    private async void RollbackHistory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: HistoryDisplayItem item } || !item.CanRollback)
        {
            return;
        }

        var decision = MessageBox.Show(
            "Restaurar as configurações salvas por esta execução? Limpezas de cache e temporários não podem ser recuperadas.",
            "Desfazer otimização",
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
