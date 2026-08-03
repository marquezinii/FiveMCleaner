using System.Net.Mail;
using System.Windows;
using FiveMCleaner.App.Services;

namespace FiveMCleaner.App.Views;

public partial class AccountWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly IFirebaseAuthService accounts;
    private bool registering;

    public AccountWindow(IFirebaseAuthService accounts)
    {
        this.accounts = accounts;
        InitializeComponent();
        accounts.StateChanged += Accounts_StateChanged;
        Loaded += (_, _) => Render(accounts.Current);
        Closed += (_, _) => accounts.StateChanged -= Accounts_StateChanged;
    }

    private void Accounts_StateChanged(object? sender, AuthenticationSnapshot state) => Dispatcher.Invoke(() => Render(state));

    private void Render(AuthenticationSnapshot state)
    {
        var verified = state.State == AuthenticationState.SignedIn;
        var verification = state.State == AuthenticationState.EmailVerificationRequired;
        AuthenticationPanel.Visibility = state.User is null ? Visibility.Visible : Visibility.Collapsed;
        VerificationPanel.Visibility = verification ? Visibility.Visible : Visibility.Collapsed;
        ManagementPanel.Visibility = verified ? Visibility.Visible : Visibility.Collapsed;
        LogoutButton.Visibility = state.User is null ? Visibility.Collapsed : Visibility.Visible;
        SubmitButton.Visibility = SwitchButton.Visibility = state.User is null ? Visibility.Visible : Visibility.Collapsed;
        if (verification) { TitleText.Text = "Verifique seu e-mail"; VerificationDetailText.Text = $"Enviamos um link de verificação para {state.User!.Email}."; }
        if (verified) { TitleText.Text = "Sua conta"; SubtitleText.Text = "Alterações sensíveis pedem sua senha atual."; SignedInEmailText.Text = state.User!.Email; }
    }

    private void Switch_Click(object sender, RoutedEventArgs e)
    {
        registering = !registering;
        RegistrationOnlyPanel.Visibility = ConfirmPanel.Visibility = TermsPanel.Visibility = PasswordPolicyText.Visibility = registering ? Visibility.Visible : Visibility.Collapsed;
        SubmitButton.Content = registering ? "Criar conta" : "Entrar";
        SwitchButton.Content = registering ? "Já possui conta? Entrar" : "Ainda não tem conta? Criar conta";
        TitleText.Text = registering ? "Crie sua conta" : "Entre na sua conta";
        StatusText.Text = string.Empty;
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidEmail(EmailBox.Text) || string.IsNullOrEmpty(PasswordBox.Password)) { Status("Informe e-mail e senha válidos.", true); return; }
        if (registering && (!AccountPasswordPolicy.IsValid(PasswordBox.Password) || PasswordBox.Password != ConfirmPasswordBox.Password || TermsCheckBox.IsChecked != true)) { Status("Use uma senha de 12 a 128 caracteres, confirme-a e aceite os Termos de Uso.", true); return; }
        await RunAsync(registering ? () => accounts.RegisterAsync(EmailBox.Text.Trim(), PasswordBox.Password, KeepSignedInBox.IsChecked == true) : () => accounts.SignInAsync(EmailBox.Text.Trim(), PasswordBox.Password, KeepSignedInBox.IsChecked == true));
    }

    private async void ResetPassword_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidEmail(EmailBox.Text)) { Status("Informe seu e-mail para receber as instruções.", true); return; }
        var result = await accounts.SendPasswordResetEmailAsync(EmailBox.Text.Trim()); Status(result.Error ?? "Se houver uma conta para este e-mail, enviaremos as instruções.", result.Error is not null);
    }
    private async void ResendVerification_Click(object sender, RoutedEventArgs e) => await RunAsync(() => accounts.ResendVerificationEmailAsync(), "Se necessário, enviamos outro e-mail de verificação.");
    private async void RefreshVerification_Click(object sender, RoutedEventArgs e) => await RunAsync(() => accounts.RefreshEmailVerificationAsync());
    private async void Logout_Click(object sender, RoutedEventArgs e) { await accounts.LogoutAsync(); DialogResult = true; }

    private async void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        if (!AccountPasswordPolicy.IsValid(NewPasswordBox.Password) || string.IsNullOrEmpty(CurrentPasswordBox.Password)) { Status("Informe a senha atual e uma nova senha de 12 a 128 caracteres.", true); return; }
        await RunAsync(() => accounts.ChangePasswordAsync(CurrentPasswordBox.Password, NewPasswordBox.Password));
    }
    private async void ChangeEmail_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidEmail(NewEmailBox.Text) || string.IsNullOrEmpty(CurrentPasswordBox.Password)) { Status("Informe o novo e-mail e sua senha atual.", true); return; }
        await RunAsync(() => accounts.ChangeEmailAsync(CurrentPasswordBox.Password, NewEmailBox.Text.Trim()));
    }
    private async void DeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(CurrentPasswordBox.Password)) { Status("Confirme sua senha atual para excluir a conta.", true); return; }
        if (System.Windows.MessageBox.Show("Excluir sua conta permanentemente?", "Excluir conta", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await RunAsync(() => accounts.DeleteAccountAsync(CurrentPasswordBox.Password));
    }

    private async Task RunAsync(Func<Task<FirebaseAuthResult>> action, string? success = null)
    {
        SubmitButton.IsEnabled = SwitchButton.IsEnabled = false;
        try { var result = await action(); Status(result.Error ?? success ?? (result.State == AuthenticationState.EmailVerificationRequired ? "Verifique seu e-mail para continuar." : string.Empty), result.Error is not null); }
        finally { SubmitButton.IsEnabled = SwitchButton.IsEnabled = true; }
    }
    private void PasswordChanged(object sender, RoutedEventArgs e) => PasswordPolicyText.Text = $"{PasswordBox.Password.Length}/128 caracteres (mínimo 12).";
    private void Terms_Click(object sender, RoutedEventArgs e) => new TermsOfUseWindow { Owner = this }.ShowDialog();
    private void Status(string text, bool error) { StatusText.Text = text; StatusText.SetResourceReference(ForegroundProperty, error ? "RedBrush" : "GreenBrush"); }
    private static bool ValidEmail(string value) => MailAddress.TryCreate(value.Trim(), out var address) && string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase);
}
