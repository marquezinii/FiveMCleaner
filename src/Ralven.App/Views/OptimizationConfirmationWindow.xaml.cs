using System.Windows;

namespace Ralven.App.Views;

/// <summary>
/// Confirma ações importantes sem recorrer ao MessageBox do Windows,
/// preservando o tema e a linguagem do aplicativo.
/// </summary>
public partial class OptimizationConfirmationWindow : Ralven.App.Controls.DialogWindow
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
        Loaded += (_, _) => DismissButton.Focus();
    }

    public string TitleText { get; }

    public string MessageText { get; }

    public string KeepWorkingText { get; }

    public string ConfirmText { get; }

    public static bool Confirm(Window? owner, string message, string title, string? confirm = null)
    {
        var strings = Services.LocalizationService.Current;
        var dialog = new OptimizationConfirmationWindow(title, message,
            strings.GetString("Dialog.Cancel"), confirm ?? strings.GetString("Dialog.Confirm"))
        { Owner = owner };
        return dialog.ShowDialog() == true;
    }

    public static void Inform(Window? owner, string message, string title)
    {
        var dialog = new OptimizationConfirmationWindow(title, message,
            Services.LocalizationService.Current.GetString("Dialog.Close"), string.Empty)
        { Owner = owner };
        dialog.DismissButton.Margin = new Thickness(0);
        dialog.ConfirmAction.Visibility = Visibility.Collapsed;
        _ = dialog.ShowDialog();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Dismiss_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
