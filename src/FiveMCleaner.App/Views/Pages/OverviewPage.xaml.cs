using System.Windows;
using System.Windows.Controls;
using FiveMCleaner.App.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace FiveMCleaner.App.Views.Pages;

/// <summary>
/// Visão geral: resume detecção, recomendação, prontidão e desempenho ao
/// vivo. Ações puramente locais chamam o <see cref="MainViewModel"/> (mesmo
/// DataContext herdado da janela); ações que cruzam página ou dependem de
/// estado do processo (baixar atualização, fechar o app) chamam de volta
/// para o <see cref="MainWindow"/>, que continua sendo o dono desse estado.
/// </summary>
public partial class OverviewPage : UserControl
{
    public OverviewPage()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private async void RefreshDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            await vm.RefreshDiagnosticAsync();
        }
    }

    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow shell)
        {
            await shell.RequestDownloadUpdateAsync();
        }
    }

    private void OpenReleaseNotes_Click(object sender, RoutedEventArgs e)
    {
        (Window.GetWindow(this) as MainWindow)?.RequestOpenReleaseNotes();
    }

    private void DismissCompletedUpdate_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.DismissCompletedUpdateBanner();
    }

    private void OpenOptimizer_Click(object sender, RoutedEventArgs e)
    {
        (Window.GetWindow(this) as MainWindow)?.RequestNavigateToOptimizer();
    }

    private void OpenHistory_Click(object sender, RoutedEventArgs e)
    {
        (Window.GetWindow(this) as MainWindow)?.RequestNavigateToHistory();
    }
}
