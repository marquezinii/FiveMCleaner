using System.Windows;
using FiveMCleaner.App.Services;

namespace FiveMCleaner.App.Views;

public partial class AccountWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly IUserAccountService accounts;
    private bool registering;

    public AccountWindow(IUserAccountService accounts)
    {
        this.accounts = accounts;
        InitializeComponent();
    }

    private void Switch_Click(object sender, RoutedEventArgs e)
    {
        registering = !registering;
        RegisterPanel.Visibility = registering ? Visibility.Visible : Visibility.Collapsed;
        ConfirmPanel.Visibility = registering ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Text = registering ? "Crie sua conta" : "Entre na sua conta";
        SubtitleText.Text = registering ? "Leva menos de um minuto e mantém sua sessão protegida." : "Acesse sua experiência FiveMCleaner em segurança.";
        SubmitButton.Content = registering ? "Criar conta" : "Entrar";
        SwitchButton.Content = registering ? "Já possui conta? Entrar" : "Ainda não tem conta? Criar conta";
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (registering && PasswordBox.Password != ConfirmPasswordBox.Password)
        {
            ShowError("As senhas não coincidem.");
            return;
        }
        SubmitButton.IsEnabled = SwitchButton.IsEnabled = false;
        var result = registering
            ? await accounts.RegisterAsync(FirstNameBox.Text, LastNameBox.Text, EmailBox.Text, PasswordBox.Password)
            : await accounts.LoginAsync(EmailBox.Text, PasswordBox.Password);
        SubmitButton.IsEnabled = SwitchButton.IsEnabled = true;
        if (result.Succeeded) DialogResult = true;
        else ShowError(result.Error ?? "Não foi possível concluir agora.");
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
