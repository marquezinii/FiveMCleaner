using System.Windows;
using System.IO;
using Ralven.App.Services;
using Ralven.App.ViewModels;

namespace Ralven.App.Controls;

public partial class UltraPanel : System.Windows.Controls.UserControl
{
    public UltraPanel() => InitializeComponent();
    private MainViewModel? ViewModel => DataContext as MainViewModel;
    private void ApplyRecommendation_Click(object sender, RoutedEventArgs e) => ViewModel?.ApplyPersonalRecommendation();
    private async void SaveProfile_Click(object sender, RoutedEventArgs e) { if (ViewModel is { } vm) await vm.SavePersonalProfileAsync(); }
    private async void StartTracking_Click(object sender, RoutedEventArgs e) { if (ViewModel is { } vm) await vm.StartPersonalTrackingAsync(); }
    private async void CheckTracking_Click(object sender, RoutedEventArgs e) { if (ViewModel is { } vm) await vm.ObservePersonalPcAsync(); }
    private async void StopTracking_Click(object sender, RoutedEventArgs e) { if (ViewModel is { } vm) await vm.StopPersonalTrackingAsync(); }
    private async void Measure_Click(object sender, RoutedEventArgs e) { if (ViewModel is { } vm) await vm.MeasurePersonalSessionAsync(); }
    private void Upgrade_Click(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.RequestNavigateToPro();
    private async void Analyze_Click(object sender, RoutedEventArgs e) { if (ViewModel is { } vm) await vm.RefreshDiagnosticAsync(); }
    private void Review_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.SelectUltra();
        RoutineExpander.IsExpanded = true;
        RoutineExpander.BringIntoView();
    }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "Ralven-personal-report.txt",
            Filter = LocalizationService.Current.GetString("Personal.Export.Filter"),
            DefaultExt = ".txt"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            var report = await vm.ExportPersonalWorkspaceAsync();
            await File.WriteAllTextAsync(dialog.FileName, report);
            vm.ReportPersonalExportResult(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            vm.ReportPersonalExportResult(false);
        }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => ViewModel?.CancelPersonalOperation();
}
