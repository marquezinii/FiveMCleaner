using System.Windows;
using System.Windows.Controls;
using Ralven.App.Services;
using Ralven.App.ViewModels;
using Ralven.App.Views;
using UserControl = System.Windows.Controls.UserControl;

namespace Ralven.App.Views.Pages;

public partial class SystemPage : UserControl
{
    public SystemPage()
    {
        InitializeComponent();
    }

    private async void SystemPage_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible && DataContext is MainViewModel viewModel)
        {
            await Task.WhenAll(
                viewModel.RefreshWindowsGamingSettingsAsync(),
                viewModel.RefreshWindowsSystemHealthAsync());
        }
    }

    private async void RefreshSystem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await Task.WhenAll(
                viewModel.RefreshWindowsGamingSettingsAsync(),
                viewModel.RefreshWindowsSystemHealthAsync());
            await viewModel.RefreshDiagnosticAsync();
        }
    }

    private async void ApplyWindowsGaming_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || !viewModel.CanApplyWindowsGamingSettings)
        {
            return;
        }

        var localization = LocalizationService.Current;
        var dialog = new OptimizationConfirmationWindow(
            localization.GetString("System.Gaming.Confirm.Title"),
            localization.GetString("System.Gaming.Confirm.Message"),
            localization.GetString("System.Gaming.Confirm.Cancel"),
            localization.GetString("System.Gaming.Confirm.Apply"))
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() == true)
        {
            await viewModel.ApplyWindowsGamingSettingsAsync();
        }
    }

    private async void RestoreWindowsGaming_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || !viewModel.CanRestoreWindowsGamingSettings)
        {
            return;
        }

        var localization = LocalizationService.Current;
        var dialog = new OptimizationConfirmationWindow(
            localization.GetString("System.Gaming.RestoreConfirm.Title"),
            localization.GetString("System.Gaming.RestoreConfirm.Message"),
            localization.GetString("System.Gaming.Confirm.Cancel"),
            localization.GetString("System.Gaming.Restore"))
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() == true)
        {
            await viewModel.RestoreWindowsGamingSettingsAsync();
        }
    }
}
