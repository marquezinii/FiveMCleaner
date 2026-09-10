using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Ralven.App.Services;
using Ralven.App.ViewModels;
using Ralven.Contracts;
using UserControl = System.Windows.Controls.UserControl;

namespace Ralven.App.Views.Pages;

/// <summary>
/// Histórico: ledger com timeline, agrupado por execução. Sem estado de
/// fase — cada linha é independente e só precisa da própria confirmação de
/// rollback.
/// </summary>
public partial class HistoryPage : UserControl
{
    public HistoryPage()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OpenOptimizer_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow shell)
        {
            shell.RequestNavigateToOptimizer(OptimizationScope.GeneralWindows);
        }
    }


    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        ExternalLauncher.TryOpen(() =>
        {
            Directory.CreateDirectory(vm.LogsDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{vm.LogsDirectory}\"",
                UseShellExecute = true
            });
        });
    }

    private async void RollbackHistory_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm
            || sender is not FrameworkElement { Tag: HistoryDisplayItem item }
            || !item.CanRollback)
        {
            return;
        }

        var isWindowsGaming = item.Kind == AppHistoryKind.WindowsGaming;
        var decision = Ralven.App.Views.OptimizationConfirmationWindow.Confirm(Window.GetWindow(this),
            LocalizationService.Current.GetString(isWindowsGaming
                ? "System.Gaming.RestoreConfirm.Message"
                : "Dialog.Rollback.Message"),
            LocalizationService.Current.GetString(isWindowsGaming
                ? "System.Gaming.RestoreConfirm.Title"
                : "Dialog.Rollback.Title"));
        if (decision == true)
        {
            await vm.RollbackAsync(item);
        }
    }

    private async void OpenHistoryReport_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm
            || sender is not FrameworkElement { Tag: HistoryDisplayItem item })
        {
            return;
        }

        if (await vm.OpenHistoryReportAsync(item)
            && Window.GetWindow(this) is MainWindow shell)
        {
            shell.RequestNavigateToOptimizerReport();
            return;
        }

        Ralven.App.Views.OptimizationConfirmationWindow.Inform(Window.GetWindow(this),
            LocalizationService.Current.GetString("History.ReportUnavailable.Message"),
            LocalizationService.Current.GetString("History.ReportUnavailable.Title"));
    }
}
