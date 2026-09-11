using System.Diagnostics;
using System.Windows;
using Forms = System.Windows.Forms;
using Ralven.App.Services;
using Ralven.App.ViewModels;
using Ralven.Contracts;
using UserControl = System.Windows.Controls.UserControl;

namespace Ralven.App.Views.Pages;

public partial class FiveMPage : UserControl
{
    private const string ReShadeDownloadUrl = "https://reshade.me/#download";

    public FiveMPage() => InitializeComponent();

    private MainWindow? Shell => Window.GetWindow(this) as MainWindow;

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void BackToGames_Click(object sender, RoutedEventArgs e) => Shell?.RequestNavigateToGames();

    private void OpenFiveMOptimizer_Click(object sender, RoutedEventArgs e) =>
        Shell?.RequestNavigateToOptimizer(OptimizationScope.FiveMLegacy);

    private void OpenWindowsOptimizer_Click(object sender, RoutedEventArgs e) =>
        Shell?.RequestNavigateToOptimizer(OptimizationScope.GeneralWindows);

    private void OpenHistory_Click(object sender, RoutedEventArgs e) => Shell?.RequestNavigateToHistory();

    private void InstallReShade_Click(object sender, RoutedEventArgs e) =>
        ExternalLauncher.TryOpen(() => Process.Start(new ProcessStartInfo(ReShadeDownloadUrl)
        {
            UseShellExecute = true
        }));

    private void ToggleFiveMSessionMonitor_Click(object sender, RoutedEventArgs e) =>
        ViewModel?.ToggleFiveMSessionMonitor();

    private async void SelectFiveMInstallation_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog();
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            await (ViewModel?.SelectFiveMInstallationAsync(dialog.SelectedPath) ?? Task.CompletedTask);
        }
    }

    private async void UseAutomaticFiveMDetection_Click(object sender, RoutedEventArgs e) =>
        await (ViewModel?.SelectFiveMInstallationAsync(null) ?? Task.CompletedTask);
}
