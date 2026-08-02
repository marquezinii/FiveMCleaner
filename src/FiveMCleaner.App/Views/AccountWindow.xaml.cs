using System.Net.Mail;
using System.Text.RegularExpressions;
using System.Windows;
using FiveMCleaner.App.Services;

namespace FiveMCleaner.App.Views;

public partial class AccountWindow : Wpf.Ui.Controls.FluentWindow
{
    private static readonly Regex PersonName = new(@"^[\p{L}\p{M}][\p{L}\p{M}' -]*$", RegexOptions.CultureInvariant);
    private static readonly Regex Username = new(@"^[a-z0-9](?:[a-z0-9._]{1,28}[a-z0-9])$", RegexOptions.CultureInvariant);
    private readonly IUserAccountService accounts;
    private bool registering;

    public AccountWindow(IUserAccountService accounts)
    {
        this.accounts = accounts;
        InitializeComponent();
        Loaded += (_, _) => EmailBox.Focus();
    }

    private void Switch_Click(object sender, RoutedEventArgs e)
    {
        registering = !registering;
        RegisterPanel.Visibility = registering ? Visibility.Visible : Visibility.Collapsed;
        ConfirmPanel.Visibility = registering ? Visibility.Visible : Visibility.Collapsed;
        TermsPanel.Visibility = registering ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Text = registering ? "Crie sua conta" : "Entre na sua conta";
        SubtitleText.Text = registering
            ? "Preencha os dados abaixo para proteger e identificar sua conta."
            : "Acesse sua experiência FiveMCleaner com segurança.";
        PasswordHelpText.Text = registering ? "Use pelo menos 10 caracteres." : "Use sua senha cadastrada.";
        SubmitButton.Content = registering ? "Criar conta" : "Entrar";
        SwitchButton.Content = registering ? "Já possui conta? Entrar" : "Ainda não tem conta? Criar conta";
        Height = registering ? 720 : 570;
        HideError();
        (registering ? FirstNameBox : EmailBox).Focus();
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateForm()) return;

        var actionLabel = registering ? "Criando conta..." : "Entrando...";
        var idleLabel = registering ? "Criar conta" : "Entrar";
        SubmitButton.IsEnabled = SwitchButton.IsEnabled = false;
        SubmitButton.Content = actionLabel;
        HideError();
        try
        {
            var result = registering
                ? await accounts.RegisterAsync(FirstNameBox.Text, LastNameBox.Text, UsernameBox.Text, EmailBox.Text, PasswordBox.Password)
                : await accounts.LoginAsync(EmailBox.Text, PasswordBox.Password);
            if (result.Succeeded) DialogResult = true;
            else ShowError(result.Error ?? "Não foi possível concluir agora.");
        }
        finally
        {
            SubmitButton.IsEnabled = SwitchButton.IsEnabled = true;
            SubmitButton.Content = idleLabel;
        }
    }

    private bool ValidateForm()
    {
        if (registering)
        {
            if (string.IsNullOrWhiteSpace(FirstNameBox.Text)) return Invalid("Informe seu nome.", FirstNameBox);
            if (!PersonName.IsMatch(FirstNameBox.Text.Trim())) return Invalid("Use um nome válido, sem números ou símbolos.", FirstNameBox);
            if (string.IsNullOrWhiteSpace(LastNameBox.Text)) return Invalid("Informe seu sobrenome.", LastNameBox);
            if (!PersonName.IsMatch(LastNameBox.Text.Trim())) return Invalid("Use um sobrenome válido, sem números ou símbolos.", LastNameBox);
            if (!Username.IsMatch(UsernameBox.Text.Trim())) return Invalid("O nome de usuário deve ter de 3 a 30 letras, números, ponto ou sublinhado.", UsernameBox);
        }

        var email = EmailBox.Text.Trim();
        if (!MailAddress.TryCreate(email, out var parsedEmail)
            || !string.Equals(parsedEmail.Address, email, StringComparison.OrdinalIgnoreCase))
            return Invalid("Informe um e-mail válido.", EmailBox);
        if (string.IsNullOrEmpty(PasswordBox.Password)) return Invalid("Informe sua senha.", PasswordBox);

        if (registering)
        {
            if (PasswordBox.Password.Length < 10) return Invalid("A senha deve ter pelo menos 10 caracteres.", PasswordBox);
            if (string.IsNullOrEmpty(ConfirmPasswordBox.Password)) return Invalid("Repita sua senha.", ConfirmPasswordBox);
            if (PasswordBox.Password != ConfirmPasswordBox.Password) return Invalid("As senhas não coincidem.", ConfirmPasswordBox);
            if (TermsCheckBox.IsChecked != true) return Invalid("Leia e aceite os Termos de Uso para criar sua conta.", TermsCheckBox);
        }

        return true;
    }

    private bool Invalid(string message, UIElement control)
    {
        ShowError(message);
        control.Focus();
        return false;
    }

    private void Terms_Click(object sender, RoutedEventArgs e) =>
        new TermsOfUseWindow { Owner = this }.ShowDialog();

    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
