using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FiveMCleaner.App.Services;
using FiveMCleaner.App.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace FiveMCleaner.App.Views.Pages;

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
            shell.RequestNavigateToOptimizer();
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

        var decision = System.Windows.MessageBox.Show(
            LocalizationService.Current.GetString("Dialog.Rollback.Message"),
            LocalizationService.Current.GetString("Dialog.Rollback.Title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (decision == MessageBoxResult.Yes)
        {
            await vm.RollbackAsync(item);
        }
    }
}
