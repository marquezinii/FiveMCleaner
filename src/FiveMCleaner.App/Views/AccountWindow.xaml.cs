using System.Net.Mail;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FiveMCleaner.App.Services;

namespace FiveMCleaner.App.Views;

public partial class AccountWindow : Wpf.Ui.Controls.FluentWindow
{
    private static readonly Regex PersonName = new(@"^[\p{L}\p{M}][\p{L}\p{M}' -]*$", RegexOptions.CultureInvariant);
    private static readonly Regex Username = new(@"^[a-z0-9](?:[a-z0-9._]{1,28}[a-z0-9])$", RegexOptions.CultureInvariant);
    private readonly IUserAccountService accounts;
    private bool registering;
    private bool synchronizingPasswords;

    public AccountWindow(IUserAccountService accounts)
    {
        this.accounts = accounts;
        InitializeComponent();
        Loaded += (_, _) => EmailBox.Focus();
    }

    private void Switch_Click(object sender, RoutedEventArgs e)
    {
        registering = !registering;
        RegisterPanel.Visibility = ConfirmPanel.Visibility = TermsPanel.Visibility = PasswordStrengthPanel.Visibility = registering ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Text = registering ? "Crie sua conta" : "Entre na sua conta";
        SubtitleText.Text = registering ? "Preencha os dados abaixo para proteger e identificar sua conta." : "Acesse sua experiência FiveMCleaner com segurança.";
        PasswordHelpText.Visibility = registering ? Visibility.Collapsed : Visibility.Visible;
        SubmitButton.Content = registering ? "Criar conta" : "Entrar";
        SwitchButton.Content = registering ? "Já possui conta? Entrar" : "Ainda não tem conta? Criar conta";
        Height = registering ? 760 : 570;
        UpdatePasswordStrength();
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
            if (string.IsNullOrWhiteSpace(FirstNameBox.Text)) return Invalid("Nome: informe seu nome.", FirstNameBox);
            if (!PersonName.IsMatch(FirstNameBox.Text.Trim())) return Invalid("Nome: use apenas letras, espaços, apóstrofo ou hífen.", FirstNameBox);
            if (!string.IsNullOrWhiteSpace(LastNameBox.Text) && !PersonName.IsMatch(LastNameBox.Text.Trim())) return Invalid("Sobrenome: use apenas letras, espaços, apóstrofo ou hífen.", LastNameBox);
            if (string.IsNullOrWhiteSpace(UsernameBox.Text)) return Invalid("Nome de usuário: informe um nome de usuário.", UsernameBox);
            if (!Username.IsMatch(UsernameBox.Text.Trim())) return Invalid("Nome de usuário: use de 3 a 30 letras, números, ponto ou sublinhado.", UsernameBox);
        }

        var email = EmailBox.Text.Trim();
        if (!MailAddress.TryCreate(email, out var parsedEmail) || !string.Equals(parsedEmail.Address, email, StringComparison.OrdinalIgnoreCase)) return Invalid("E-mail: informe um endereço válido.", EmailBox);
        if (string.IsNullOrEmpty(PasswordBox.Password)) return Invalid("Senha: informe sua senha.", PasswordBox);
        if (registering)
        {
            if (!AccountPasswordPolicy.IsValid(PasswordBox.Password)) return Invalid("Senha: cumpra todos os requisitos indicados abaixo do campo.", PasswordBox);
            if (string.IsNullOrEmpty(ConfirmPasswordBox.Password)) return Invalid("Repetir senha: confirme sua senha.", ConfirmPasswordBox);
            if (PasswordBox.Password != ConfirmPasswordBox.Password) return Invalid("Repetir senha: as senhas não coincidem.", ConfirmPasswordBox);
            if (TermsCheckBox.IsChecked != true) return Invalid("Termos de Uso: marque a caixa para aceitar os termos e criar sua conta.", TermsCheckBox);
        }
        return true;
    }

    private void PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (synchronizingPasswords) return;
        synchronizingPasswords = true;
        PasswordVisibleBox.Text = PasswordBox.Password;
        synchronizingPasswords = false;
        UpdatePasswordStrength();
    }

    private void PasswordVisibleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (synchronizingPasswords) return;
        synchronizingPasswords = true;
        PasswordBox.Password = PasswordVisibleBox.Text;
        synchronizingPasswords = false;
        UpdatePasswordStrength();
    }

    private void ConfirmPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (synchronizingPasswords) return;
        synchronizingPasswords = true;
        ConfirmPasswordVisibleBox.Text = ConfirmPasswordBox.Password;
        synchronizingPasswords = false;
    }

    private void ConfirmPasswordVisibleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (synchronizingPasswords) return;
        synchronizingPasswords = true;
        ConfirmPasswordBox.Password = ConfirmPasswordVisibleBox.Text;
        synchronizingPasswords = false;
    }

    private void PasswordVisibilityButton_Click(object sender, RoutedEventArgs e) => TogglePasswordVisibility(PasswordBox, PasswordVisibleBox, PasswordVisibilityButton);
    private void ConfirmPasswordVisibilityButton_Click(object sender, RoutedEventArgs e) => TogglePasswordVisibility(ConfirmPasswordBox, ConfirmPasswordVisibleBox, ConfirmPasswordVisibilityButton);

    private static void TogglePasswordVisibility(PasswordBox passwordBox, System.Windows.Controls.TextBox visibleBox, System.Windows.Controls.Button button)
    {
        var visible = visibleBox.Visibility != Visibility.Visible;
        visibleBox.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        passwordBox.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        button.ToolTip = visible ? "Ocultar senha" : "Mostrar senha";
        button.SetValue(AutomationProperties.NameProperty, visible ? "Ocultar senha" : "Mostrar senha");
        if (visible) { visibleBox.Focus(); visibleBox.CaretIndex = visibleBox.Text.Length; }
        else passwordBox.Focus();
    }

    private void UpdatePasswordStrength()
    {
        var requirements = AccountPasswordPolicy.Evaluate(PasswordBox.Password);
        SetRequirement(LengthRequirementText, requirements.HasMinimumLength, $"{AccountPasswordPolicy.MinimumLength}+ caracteres");
        SetRequirement(CaseRequirementText, requirements.HasUppercase && requirements.HasLowercase, "letra maiúscula e minúscula");
        SetRequirement(NumberRequirementText, requirements.HasNumber, "um número");
        SetRequirement(SpecialRequirementText, requirements.HasSpecialCharacter, "um caractere especial");
        var score = requirements.CompletedCount;
        PasswordStrengthFill.Width = score * 106;
        var (label, brush) = score switch { 5 => ("Excelente", "GreenBrush"), 4 => ("Boa", "BlueBrush"), 3 => ("Razoável", "OrangeLightBrush"), _ => ("Fraca", "RedBrush") };
        PasswordStrengthText.Text = label;
        PasswordStrengthText.SetResourceReference(ForegroundProperty, brush);
        PasswordStrengthFill.SetResourceReference(BackgroundProperty, brush);
    }

    private static void SetRequirement(TextBlock text, bool satisfied, string label)
    {
        text.Text = $"{(satisfied ? "✓" : "×")} {label}";
        text.SetResourceReference(ForegroundProperty, satisfied ? "GreenBrush" : "RedBrush");
    }

    private bool Invalid(string message, UIElement control) { ShowError(message); control.Focus(); return false; }
    private void Terms_Click(object sender, RoutedEventArgs e) => new TermsOfUseWindow { Owner = this }.ShowDialog();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void ShowError(string message) { ErrorText.Text = message; ErrorText.Visibility = Visibility.Visible; }
}
