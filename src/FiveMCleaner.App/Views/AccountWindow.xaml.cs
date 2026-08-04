using System.Windows;
using FiveMCleaner.App.Services;

namespace FiveMCleaner.App.Views;

public partial class AccountWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly IFirebaseAuthService accounts;
    private readonly IAccountProfileService profiles;
    private bool registering;

    /// <summary>
    /// True right after a successful Firebase registration whose profile
    /// (username/nome/sobrenome) failed to save -- most commonly because the
    /// username was already taken. The Firebase account is real and kept;
    /// the window narrows down to just the profile fields so the user can
    /// pick a different username without redoing e-mail/senha or losing the
    /// account. See <see cref="SaveProfileAsync"/>.
    /// </summary>
    private bool requiresProfileSetup;

    public AccountWindow(IFirebaseAuthService accounts, IAccountProfileService profiles)
    {
        this.accounts = accounts;
        this.profiles = profiles;
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
        var hasUser = state.User is not null;
        var showRegistrationExtras = registering && !requiresProfileSetup;

        AuthenticationPanel.Visibility = !hasUser || requiresProfileSetup ? Visibility.Visible : Visibility.Collapsed;
        ProfileFieldsPanel.Visibility = registering || requiresProfileSetup ? Visibility.Visible : Visibility.Collapsed;
        CredentialFieldsPanel.Visibility = requiresProfileSetup ? Visibility.Collapsed : Visibility.Visible;
        RegistrationOnlyPanel.Visibility = ConfirmPanel.Visibility = TermsPanel.Visibility = PasswordPolicyText.Visibility = showRegistrationExtras ? Visibility.Visible : Visibility.Collapsed;
        ResetPasswordButton.Visibility = requiresProfileSetup ? Visibility.Collapsed : Visibility.Visible;

        VerificationPanel.Visibility = verification && !requiresProfileSetup ? Visibility.Visible : Visibility.Collapsed;
        ManagementPanel.Visibility = verified && !requiresProfileSetup ? Visibility.Visible : Visibility.Collapsed;
        LogoutButton.Visibility = hasUser && !requiresProfileSetup ? Visibility.Visible : Visibility.Collapsed;
        SubmitButton.Visibility = !hasUser || requiresProfileSetup ? Visibility.Visible : Visibility.Collapsed;
        SwitchButton.Visibility = !hasUser && !requiresProfileSetup ? Visibility.Visible : Visibility.Collapsed;
        SubmitButton.Content = requiresProfileSetup ? "Salvar perfil" : registering ? "Criar conta" : "Entrar";

        if (requiresProfileSetup) { TitleText.Text = "Complete seu cadastro"; SubtitleText.Text = "Escolha um nome de usuário disponível para continuar."; }
        else if (verification) { TitleText.Text = "Verifique seu e-mail"; VerificationDetailText.Text = $"Enviamos um link de verificação para {state.User!.Email}. Não chegou? Use o botão abaixo para reenviar."; }
        else if (verified) { TitleText.Text = "Sua conta"; SubtitleText.Text = "Alterações sensíveis pedem sua senha atual."; SignedInEmailText.Text = state.User!.Email; }
    }

    private void Switch_Click(object sender, RoutedEventArgs e)
    {
        registering = !registering;
        RegistrationOnlyPanel.Visibility = ConfirmPanel.Visibility = TermsPanel.Visibility = PasswordPolicyText.Visibility = ProfileFieldsPanel.Visibility = registering ? Visibility.Visible : Visibility.Collapsed;
        SubmitButton.Content = registering ? "Criar conta" : "Entrar";
        SwitchButton.Content = registering ? "Já possui conta? Entrar" : "Ainda não tem conta? Criar conta";
        TitleText.Text = registering ? "Crie sua conta" : "Entre na sua conta";
        StatusText.Text = string.Empty;
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (requiresProfileSetup) { await SubmitProfileAsync(); return; }

        if (!AccountValidation.IsValidEmail(EmailBox.Text) || string.IsNullOrEmpty(PasswordBox.Password)) { Status("Informe e-mail e senha válidos.", true); return; }

        if (registering)
        {
            if (!AccountValidation.IsValidPersonName(FirstNameBox.Text) || !AccountValidation.IsValidPersonName(LastNameBox.Text)) { Status("Informe seu nome e sobrenome.", true); return; }
            if (!AccountValidation.IsValidUsername(UsernameBox.Text)) { Status("O nome de usuário deve ter de 3 a 24 caracteres, começar com uma letra e usar apenas letras, números e \"_\".", true); return; }
            if (!AccountPasswordPolicy.IsValid(PasswordBox.Password) || PasswordBox.Password != ConfirmPasswordBox.Password || TermsCheckBox.IsChecked != true) { Status("A senha deve possuir pelo menos 12 caracteres, confirme-a e aceite os Termos de Uso.", true); return; }
            await SubmitRegistrationAsync();
            return;
        }

        await RunAsync(() => accounts.SignInAsync(EmailBox.Text.Trim(), PasswordBox.Password, KeepSignedInBox.IsChecked == true));
    }

    private async Task SubmitRegistrationAsync()
    {
        SubmitButton.IsEnabled = SwitchButton.IsEnabled = false;
        try
        {
            var result = await accounts.RegisterAsync(EmailBox.Text.Trim(), PasswordBox.Password, KeepSignedInBox.IsChecked == true);
            if (result.Error is not null) { Status(result.Error, true); return; }
            await SaveProfileAsync();
        }
        finally { SubmitButton.IsEnabled = SwitchButton.IsEnabled = true; }
    }

    private async Task SubmitProfileAsync()
    {
        if (!AccountValidation.IsValidPersonName(FirstNameBox.Text) || !AccountValidation.IsValidPersonName(LastNameBox.Text)) { Status("Informe seu nome e sobrenome.", true); return; }
        if (!AccountValidation.IsValidUsername(UsernameBox.Text)) { Status("O nome de usuário deve ter de 3 a 24 caracteres, começar com uma letra e usar apenas letras, números e \"_\".", true); return; }
        SubmitButton.IsEnabled = false;
        try { await SaveProfileAsync(); } finally { SubmitButton.IsEnabled = true; }
    }

    private async Task SaveProfileAsync()
    {
        var token = await accounts.GetIdTokenAsync();
        if (token is null) { requiresProfileSetup = true; Status("Sessão expirada. Entre novamente para concluir seu cadastro.", true); Render(accounts.Current); return; }

        var submission = new AccountProfileSubmission { Username = UsernameBox.Text.Trim(), FirstName = FirstNameBox.Text.Trim(), LastName = LastNameBox.Text.Trim() };
        var result = await profiles.CreateAsync(token, submission);
        requiresProfileSetup = result.Outcome != AccountProfileOutcome.Created;
        Status(requiresProfileSetup ? result.Message ?? "Não foi possível salvar seu perfil." : string.Empty, requiresProfileSetup);
        Render(accounts.Current);
    }

    private async void ResetPassword_Click(object sender, RoutedEventArgs e)
    {
        if (!AccountValidation.IsValidEmail(EmailBox.Text)) { Status("Informe seu e-mail para receber as instruções.", true); return; }
        var result = await accounts.SendPasswordResetEmailAsync(EmailBox.Text.Trim()); Status(result.Error ?? "Se houver uma conta para este e-mail, enviaremos as instruções.", result.Error is not null);
    }
    private async void ResendVerification_Click(object sender, RoutedEventArgs e) => await RunAsync(() => accounts.ResendVerificationEmailAsync(), "Se necessário, enviamos outro e-mail de verificação.");
    private async void RefreshVerification_Click(object sender, RoutedEventArgs e) => await RunAsync(() => accounts.RefreshEmailVerificationAsync());
    private async void Logout_Click(object sender, RoutedEventArgs e) { await accounts.LogoutAsync(); DialogResult = true; }

    private async void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        if (!AccountPasswordPolicy.IsValid(NewPasswordBox.Password) || string.IsNullOrEmpty(CurrentPasswordBox.Password)) { Status("Informe a senha atual e uma nova senha com pelo menos 12 caracteres.", true); return; }
        await RunAsync(() => accounts.ChangePasswordAsync(CurrentPasswordBox.Password, NewPasswordBox.Password));
    }
    private async void ChangeEmail_Click(object sender, RoutedEventArgs e)
    {
        if (!AccountValidation.IsValidEmail(NewEmailBox.Text) || string.IsNullOrEmpty(CurrentPasswordBox.Password)) { Status("Informe o novo e-mail e sua senha atual.", true); return; }
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
    private void PasswordChanged(object sender, RoutedEventArgs e) => PasswordPolicyText.Text = $"{PasswordBox.Password.Length} caracteres (mínimo 12).";
    private void Terms_Click(object sender, RoutedEventArgs e) => new TermsOfUseWindow { Owner = this }.ShowDialog();
    private void Status(string text, bool error) { StatusText.Text = text; StatusText.SetResourceReference(ForegroundProperty, error ? "RedBrush" : "GreenBrush"); }
}
