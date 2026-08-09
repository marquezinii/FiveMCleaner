using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using FiveMCleaner.App.ViewModels;
using FiveMCleaner.Contracts;
using UserControl = System.Windows.Controls.UserControl;

namespace FiveMCleaner.App.Views.Pages;

/// <summary>
/// Otimizador: um único painel com três conteúdos mutuamente exclusivos
/// (preparar/executar/resultado) e a lista de ações sempre visível abaixo. O
/// <see cref="Controls.SpectrumSelector"/> substitui a antiga combinação de
/// hero recomendado + três cards de perfil por um único sistema visual.
/// </summary>
public partial class OptimizerPage : UserControl
{
    private bool syncingProfileSelection;

    public OptimizerPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm)
        {
            oldVm.PropertyChanged -= ViewModel_PropertyChanged;
        }

        if (e.NewValue is MainViewModel newVm)
        {
            newVm.PropertyChanged += ViewModel_PropertyChanged;
            SyncProfileSelectorFromViewModel(newVm);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainViewModel vm)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsLightSelected):
            case nameof(MainViewModel.IsBalancedSelected):
            case nameof(MainViewModel.IsAggressiveSelected):
            case nameof(MainViewModel.IsLightRecommended):
            case nameof(MainViewModel.IsBalancedRecommended):
            case nameof(MainViewModel.IsAggressiveRecommended):
                SyncProfileSelectorFromViewModel(vm);
                break;
        }
    }

    private void SyncProfileSelectorFromViewModel(MainViewModel vm)
    {
        syncingProfileSelection = true;
        try
        {
            ProfileSpectrum.SelectedIndex = vm.IsAggressiveSelected ? 2 : vm.IsBalancedSelected ? 1 : 0;
            ProfileSpectrum.RecommendedIndex = vm.IsAggressiveRecommended ? 2 : vm.IsBalancedRecommended ? 1 : 0;
        }
        finally
        {
            syncingProfileSelection = false;
        }
    }

    private void ProfileSpectrum_SelectionChanged(object? sender, EventArgs e)
    {
        if (syncingProfileSelection || ViewModel is not { } vm)
        {
            return;
        }

        vm.SelectProfile(ProfileSpectrum.SelectedIndex switch
        {
            2 => OptimizationProfile.Aggressive,
            1 => OptimizationProfile.Balanced,
            _ => OptimizationProfile.Light
        });
    }

    private async void StartOptimization_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow shell)
        {
            await shell.RequestStartOptimizationAsync();
        }
    }

    private void ToggleStepLedger_Click(object sender, RoutedEventArgs e)
    {
        StepLedgerList.Visibility = StepLedgerList.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void CancelOptimization_Click(object sender, RoutedEventArgs e)
    {
        (Window.GetWindow(this) as MainWindow)?.RequestCancelOptimization();
    }

    private void CopyTechnicalReport_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.CopyTechnicalReport();
    }

    private void SaveTechnicalReport_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { CanShareReport: true } vm)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = vm.SuggestedReportFileName,
            DefaultExt = ".txt",
            Filter = "Text (*.txt)|*.txt|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            vm.SaveTechnicalReport(dialog.FileName);
        }
    }

    private async void RevertLastOptimization_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { CanRevertLastOptimization: true } vm)
        {
            await vm.RevertLastOptimizationAsync();
        }
    }
}
