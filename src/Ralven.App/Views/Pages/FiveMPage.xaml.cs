using System.Windows;
using Ralven.Contracts;
using UserControl = System.Windows.Controls.UserControl;

namespace Ralven.App.Views.Pages;

public partial class FiveMPage : UserControl
{
    public FiveMPage() => InitializeComponent();

    private MainWindow? Shell => Window.GetWindow(this) as MainWindow;

    private void BackToGames_Click(object sender, RoutedEventArgs e) => Shell?.RequestNavigateToGames();

    private void OpenFiveMOptimizer_Click(object sender, RoutedEventArgs e) =>
        Shell?.RequestNavigateToOptimizer(OptimizationScope.FiveMLegacy);

    private void OpenWindowsOptimizer_Click(object sender, RoutedEventArgs e) =>
        Shell?.RequestNavigateToOptimizer(OptimizationScope.GeneralWindows);

    private void OpenHistory_Click(object sender, RoutedEventArgs e) => Shell?.RequestNavigateToHistory();
}
