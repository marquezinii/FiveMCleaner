using System.Windows;
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
    private void Cancel_Click(object sender, RoutedEventArgs e) => ViewModel?.CancelPersonalOperation();
}
