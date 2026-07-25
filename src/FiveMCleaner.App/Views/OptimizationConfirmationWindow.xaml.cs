using System.Windows;

namespace FiveMCleaner.App.Views;

/// <summary>
/// Confirma interrupções de uma otimização sem recorrer ao MessageBox do
/// Windows, preservando o tema e a linguagem do aplicativo.
/// </summary>
public partial class OptimizationConfirmationWindow : Window
{
    public OptimizationConfirmationWindow(
        string title,
        string message,
        string keepWorking,
        string confirm)
    {
        TitleText = title;
        MessageText = message;
        KeepWorkingText = keepWorking;
        ConfirmText = confirm;
        InitializeComponent();
        DataContext = this;
    }

    public string TitleText { get; }

    public string MessageText { get; }

    public string KeepWorkingText { get; }

    public string ConfirmText { get; }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Dismiss_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
