using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FiveMCleaner.App.Services;

namespace FiveMCleaner.App.Views;

public partial class AccountWindow : Wpf.Ui.Controls.FluentWindow
{
    /// <summary>
    /// Idle time after the last keystroke before the username is checked
    /// against the server. Long enough that typing a name end to end costs
    /// one request, short enough to feel immediate.
    /// </summary>
    private static readonly TimeSpan UsernameProbeDelay = TimeSpan.FromMilliseconds(450);

    private readonly IFirebaseAuthService accounts;
    private readonly IAccountProfileService profiles;
    private readonly IGoogleOAuthClient googleOAuth;
    private bool registering;

    /// <summary>Cancels the in-flight username probe when the user keeps typing.</summary>
    private CancellationTokenSource? usernameProbe;

    /// <summary>
    /// True right after a successful Firebase registration whose profile
    /// (username/nome/sobrenome) failed to save -- most commonly because the
    /// username was already taken -- and also right after a first-time Google
    /// sign-in, which produces a real Firebase account with no profile row.
    /// The Firebase account is real and kept; the window narrows down to just
    /// the profile fields so the user can pick a username without redoing
    /// anything or losing the account. See <see cref="SaveProfileAsync"/>.
    /// </summary>
    private bool requiresProfileSetup;

    public AccountWindow(IFirebaseAuthService accounts, IAccountProfileService profiles, IGoogleOAuthClient googleOAuth)
    {
        this.accounts = accounts;
        this.profiles = profiles;
        this.googleOAuth = googleOAuth;
        InitializeComponent();
        accounts.StateChanged += Accounts_StateChanged;
        Loaded += (_, _) => Render(accounts.Current);
        Closed += (_, _) =>
        {
            accounts.StateChanged -= Accounts_StateChanged;
            usernameProbe?.Cancel();
            usernameProbe?.Dispose();
        };
    }

    private void Accounts_StateChanged(object? sender, AuthenticationSnapshot state) => Dispatcher.Invoke(() => Render(state));

    /// <summary>Esc cancels, exactly like the X in the title bar.</summary>
    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }

    private void Render(AuthenticationSnapshot state)
    {
        var verified = state.State == AuthenticationState.SignedIn;
        var verification = state.State == AuthenticationState.EmailVerificationRequired;
        var hasUser = state.User is not null;
        var showRegistrationExtras = registering && !requiresProfileSetup;
        var collectingCredentials = !hasUser || requiresProfileSetup;

        AuthenticationPanel.Visibility = Show(collectingCredentials);
        ProfileFieldsPanel.Visibility = Show(registering || requiresProfileSetup);
        CredentialFieldsPanel.Visibility = Show(!requiresProfileSetup);
        CredentialsSectionLabel.Visibility = ConfirmPanel.Visibility = TermsPanel.Visibility =
            PasswordPolicyPanel.Visibility = Show(showRegistrationExtras);

        // The external provider only makes sense before an account exists.
        // Once Firebase has authenticated someone -- including the Google
        // user still choosing a username -- offering it again would just
        // restart a flow that already succeeded.
        ProviderPanel.Visibility = Show(!hasUser && googleOAuth.IsConfigured);

        VerificationPanel.Visibility = Show(verification && !requiresProfileSetup);
        ManagementPanel.Visibility = Show(verified && !requiresProfileSetup);
        LogoutButton.Visibility = Show(hasUser && !requiresProfileSetup);
        SubmitButton.Visibility = Show(collectingCredentials);
        SwitchButton.Visibility = Show(!hasUser && !requiresProfileSetup);

        SubmitButton.Content = requiresProfileSetup ? "Concluir cadastro" : registering ? "Criar minha conta" : "Entrar";

        if (requiresProfileSetup)
        {
            TitleText.Text = "Falta pouco";
            SubtitleText.Text = "Escolha um nome de usuário disponível para concluir seu cadastro.";
            // Nothing to recover with when there is no password in this flow.
            ResetPasswordButton.Visibility = Visibility.Collapsed;
        }
        else if (verification)
        {
            TitleText.Text = "Confirme seu e-mail";
            SubtitleText.Text = "Só falta um clique no link que enviamos.";
            VerificationDetailText.Text =
                $"Enviamos um link de confirmação para {state.User!.Email}. Abra o e-mail e clique no link — se não chegar em alguns minutos, confira o spam ou reenvie abaixo.";
        }
        else if (verified)
        {
            TitleText.Text = "Sua conta";
            SubtitleText.Text = "Alterações de senha e e-mail pedem a confirmação da sua senha atual.";
            SignedInEmailText.Text = state.User!.Email;
        }
        else
        {
            ApplyModeCopy();
        }
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Everything that reads differently between "entrar" and "cadastre-se".
    /// Kept in one place so the two modes can never drift into the mixed,
    /// half-updated wording the window used to show after a switch.
    /// </summary>
    private void ApplyModeCopy()
    {
        TitleText.Text = registering ? "Crie sua conta" : "Bem-vindo de volta";
        SubtitleText.Text = registering
            ? "Leva menos de um minuto. Depois é só confirmar seu e-mail."
            : "Entre para sincronizar suas preferências entre instalações.";
        SubmitButton.Content = registering ? "Criar minha conta" : "Entrar";
        SwitchButton.Content = registering ? "Já tem uma conta? Entrar" : "Ainda não tem conta? Criar conta";
        GoogleButtonText.Text = registering ? "Cadastrar-se com o Google" : "Continuar com o Google";
        ProviderDividerText.Text = registering ? "OU CADASTRE-SE COM E-MAIL" : "OU USE SEU E-MAIL";
        PasswordField.Placeholder = registering ? "Crie uma senha forte" : "Sua senha";
    }

    private void Switch_Click(object sender, RoutedEventArgs e)
    {
        registering = !registering;

        // "Esqueci minha senha" belongs to the sign-in form only, and even
        // there only after an attempt failed -- switching modes resets it.
        ResetPasswordButton.Visibility = Visibility.Collapsed;
        ClearStatus();
        ConfirmPasswordField.Clear();
        ResetUsernameStatus();
        UpdatePasswordFeedback();
        Render(accounts.Current);
    }

    // ===================== Envio do formulário =====================

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (requiresProfileSetup) { await SubmitProfileAsync(); return; }
        if (registering) { await SubmitRegistrationAsync(); return; }
        await SubmitSignInAsync();
    }

    private async Task SubmitSignInAsync()
    {
        if (!AccountValidation.IsValidEmail(EmailBox.Text))
        {
            Status("Informe um endereço de e-mail válido.", true);
            EmailBox.Focus();
            return;
        }

        if (PasswordField.Password.Length == 0)
        {
            Status("Digite sua senha para entrar.", true);
            PasswordField.Focus();
            return;
        }

        var result = await RunAsync(() => accounts.SignInAsync(EmailBox.Text.Trim(), PasswordField.Password, KeepSignedInBox.IsChecked == true));

        // Firebase deliberately does not tell us whether it was the e-mail or
        // the password that was wrong (that would let anyone probe which
        // addresses have accounts), so "the sign-in attempt failed" is the
        // most precise trigger available -- and it is exactly the moment the
        // recovery link becomes useful.
        if (result is { Succeeded: false, Error: not null })
        {
            ResetPasswordButton.Visibility = Visibility.Visible;
        }
    }

    private async Task SubmitRegistrationAsync()
    {
        if (!ValidateRegistrationFields()) return;

        SetBusy(true);
        try
        {
            var result = await accounts.RegisterAsync(EmailBox.Text.Trim(), PasswordField.Password, KeepSignedInBox.IsChecked == true);
            if (result.Error is not null) { Status(result.Error, true); return; }
            await SaveProfileAsync();
        }
        finally { SetBusy(false); }
    }

    /// <summary>
    /// Validates the registration form field by field, in reading order, and
    /// focuses the first offender. One message per problem: the old single
    /// "senha de 12 caracteres, confirme e aceite os termos" sentence made
    /// the user guess which of the three actually blocked them.
    /// </summary>
    private bool ValidateRegistrationFields()
    {
        if (!AccountValidation.IsValidPersonName(FirstNameBox.Text)) { return Reject("Informe seu nome.", FirstNameBox); }
        if (!AccountValidation.IsValidPersonName(LastNameBox.Text)) { return Reject("Informe seu sobrenome.", LastNameBox); }
        if (!AccountValidation.IsValidUsername(UsernameBox.Text))
        {
            return Reject("O nome de usuário deve ter de 3 a 24 caracteres, começar com uma letra e usar apenas letras, números e \"_\".", UsernameBox);
        }
        if (!AccountValidation.IsValidEmail(EmailBox.Text)) { return Reject("Informe um endereço de e-mail válido.", EmailBox); }
        if (!AccountPasswordPolicy.IsValid(PasswordField.Password))
        {
            return Reject("Sua senha precisa de pelo menos 12 caracteres.", PasswordField);
        }
        if (PasswordField.Password != ConfirmPasswordField.Password)
        {
            return Reject("As duas senhas precisam ser iguais.", ConfirmPasswordField);
        }
        if (TermsCheckBox.IsChecked != true)
        {
            Status("Aceite os Termos de Uso para criar sua conta.", true);
            TermsCheckBox.Focus();
            return false;
        }
        return true;
    }

    private bool Reject(string message, UIElement field)
    {
        Status(message, true);
        field.Focus();
        return false;
    }

    private async Task SubmitProfileAsync()
    {
        if (!AccountValidation.IsValidPersonName(FirstNameBox.Text)) { Reject("Informe seu nome.", FirstNameBox); return; }
        if (!AccountValidation.IsValidPersonName(LastNameBox.Text)) { Reject("Informe seu sobrenome.", LastNameBox); return; }
        if (!AccountValidation.IsValidUsername(UsernameBox.Text))
        {
            Reject("O nome de usuário deve ter de 3 a 24 caracteres, começar com uma letra e usar apenas letras, números e \"_\".", UsernameBox);
            return;
        }

        SetBusy(true);
        try { await SaveProfileAsync(); } finally { SetBusy(false); }
    }

    private async Task SaveProfileAsync()
    {
        var token = await accounts.GetIdTokenAsync();
        if (token is null)
        {
            requiresProfileSetup = true;
            Status("Sua sessão expirou. Entre novamente para concluir seu cadastro.", true);
            Render(accounts.Current);
            return;
        }

        var submission = new AccountProfileSubmission
        {
            Username = UsernameBox.Text.Trim(),
            FirstName = FirstNameBox.Text.Trim(),
            LastName = LastNameBox.Text.Trim(),
        };
        var result = await profiles.CreateAsync(token, submission);
        requiresProfileSetup = result.Outcome != AccountProfileOutcome.Created;

        if (requiresProfileSetup)
        {
            Status(result.Message ?? "Não foi possível salvar seu perfil.", true);
            if (result.Outcome == AccountProfileOutcome.UsernameTaken)
            {
                ShowUsernameStatus(UsernameAvailability.Taken);
                UsernameBox.Focus();
                UsernameBox.SelectAll();
            }
        }
        else
        {
            ClearStatus();
        }

        Render(accounts.Current);
    }

    // ===================== Entrar com o Google =====================

    private async void GoogleSignIn_Click(object sender, RoutedEventArgs e)
    {
        ClearStatus();
        SetBusy(true);
        try
        {
            Status("Concluindo o login na janela do seu navegador…", false);
            var ticket = await googleOAuth.AuthenticateAsync();
            if (ticket.IdToken is null) { Status(ticket.Error ?? "Não foi possível entrar com o Google.", true); return; }

            var federated = await accounts.SignInWithGoogleAsync(ticket.IdToken, KeepSignedInBox.IsChecked == true);
            if (!federated.Result.Succeeded)
            {
                Status(federated.Result.Error ?? "Não foi possível entrar com o Google.", true);
                return;
            }

            await ContinueAfterGoogleAsync(federated);
        }
        finally { SetBusy(false); }
    }

    /// <summary>
    /// A Google account is a real Firebase account, but Firebase never stores
    /// the username/nome/sobrenome this app requires. Accounts signing in for
    /// the first time therefore land on the profile step with the names
    /// Google already provided prefilled; returning accounts go straight in.
    /// </summary>
    private async Task ContinueAfterGoogleAsync(FederatedSignInResult federated)
    {
        var token = await accounts.GetIdTokenAsync();
        var existing = token is null
            ? new AccountProfileFetchResult(AccountProfileFetchOutcome.Failed)
            : await profiles.FetchAsync(token);

        if (existing.Outcome == AccountProfileFetchOutcome.Found)
        {
            DialogResult = true;
            return;
        }

        if (existing.Outcome == AccountProfileFetchOutcome.Failed)
        {
            // The account is signed in and valid; only the profile lookup
            // failed (offline, Worker down). Forcing the profile step here
            // would risk a duplicate row for a name the user already owns.
            DialogResult = true;
            return;
        }

        requiresProfileSetup = true;
        if (string.IsNullOrWhiteSpace(FirstNameBox.Text) && !string.IsNullOrWhiteSpace(federated.FirstName))
        {
            FirstNameBox.Text = federated.FirstName;
        }
        if (string.IsNullOrWhiteSpace(LastNameBox.Text) && !string.IsNullOrWhiteSpace(federated.LastName))
        {
            LastNameBox.Text = federated.LastName;
        }

        ClearStatus();
        Render(accounts.Current);
        UsernameBox.Focus();
    }

    // ===================== Nome de usuário =====================

    private async void Username_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        usernameProbe?.Cancel();
        usernameProbe?.Dispose();
        usernameProbe = null;

        var candidate = UsernameBox.Text.Trim();
        if (candidate.Length == 0) { ResetUsernameStatus(); return; }

        if (!AccountValidation.IsValidUsername(candidate))
        {
            // The format rule is already spelled out under the field; a red
            // duplicate of it on every keystroke would be noise.
            ResetUsernameStatus();
            return;
        }

        ShowUsernameChecking();

        var probe = new CancellationTokenSource();
        usernameProbe = probe;
        try
        {
            await Task.Delay(UsernameProbeDelay, probe.Token);
            var availability = await profiles.CheckUsernameAsync(candidate, probe.Token);
            if (probe.Token.IsCancellationRequested) return;
            ShowUsernameStatus(availability);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer keystroke: the newer probe owns the label.
        }
    }

    private void ResetUsernameStatus() => UsernameStatusPanel.Visibility = Visibility.Collapsed;

    private void ShowUsernameChecking()
    {
        UsernameStatusPanel.Visibility = Visibility.Visible;
        UsernameStatusIcon.Data = (Geometry)FindResource("IconRefresh");
        UsernameStatusIcon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "TextSubtleBrush");
        UsernameStatusText.SetResourceReference(ForegroundProperty, "TextSubtleBrush");
        UsernameStatusText.Text = "Verificando disponibilidade…";
    }

    private void ShowUsernameStatus(UsernameAvailability availability)
    {
        // Unknown must never look like a green light: the name may well be
        // taken, we simply could not ask.
        if (availability is UsernameAvailability.Unknown or UsernameAvailability.Invalid)
        {
            ResetUsernameStatus();
            return;
        }

        var available = availability == UsernameAvailability.Available;
        UsernameStatusPanel.Visibility = Visibility.Visible;
        UsernameStatusIcon.Data = (Geometry)FindResource(available ? "IconCheck" : "IconClose");
        UsernameStatusIcon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, available ? "GreenBrush" : "RedBrush");
        UsernameStatusText.SetResourceReference(ForegroundProperty, available ? "GreenBrush" : "RedBrush");
        UsernameStatusText.Text = available
            ? "Este nome de usuário está disponível."
            : "Este nome de usuário já está em uso.";
    }

    // ===================== Senha =====================

    private void Password_Changed(object? sender, EventArgs e) => UpdatePasswordFeedback();

    private void UpdatePasswordFeedback()
    {
        var length = PasswordField.Password.Length;
        PasswordStrengthBar.Value = Math.Min(length, AccountPasswordPolicy.MinimumLength);
        PasswordPolicyText.Text = length == 0
            ? $"Use pelo menos {AccountPasswordPolicy.MinimumLength} caracteres."
            : length < AccountPasswordPolicy.MinimumLength
                ? $"Faltam {AccountPasswordPolicy.MinimumLength - length} caractere(s) para o mínimo de {AccountPasswordPolicy.MinimumLength}."
                : "Boa! Sua senha atende ao mínimo exigido.";
        PasswordStrengthBar.SetResourceReference(
            ForegroundProperty,
            length >= AccountPasswordPolicy.MinimumLength ? "GreenBrush" : length >= AccountPasswordPolicy.MinimumLength / 2 ? "YellowBrush" : "RedBrush");

        UpdateConfirmFeedback();
    }

    /// <summary>
    /// Live "do the two passwords match?" readout. The submit path checks
    /// the same equality (<see cref="ValidateRegistrationFields"/>); this is
    /// what makes the answer visible before the user commits.
    /// </summary>
    private void UpdateConfirmFeedback()
    {
        if (ConfirmPanel.Visibility != Visibility.Visible || ConfirmPasswordField.Password.Length == 0)
        {
            ConfirmStatusPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var matches = PasswordField.Password == ConfirmPasswordField.Password;
        ConfirmStatusPanel.Visibility = Visibility.Visible;
        ConfirmStatusIcon.Data = (Geometry)FindResource(matches ? "IconCheck" : "IconClose");
        ConfirmStatusIcon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, matches ? "GreenBrush" : "RedBrush");
        ConfirmStatusText.SetResourceReference(ForegroundProperty, matches ? "GreenBrush" : "RedBrush");
        ConfirmStatusText.Text = matches ? "As senhas coincidem." : "As senhas não coincidem.";
    }

    // ===================== Demais ações =====================

    private async void ResetPassword_Click(object sender, RoutedEventArgs e)
    {
        if (!AccountValidation.IsValidEmail(EmailBox.Text))
        {
            Reject("Informe seu e-mail para receber as instruções de recuperação.", EmailBox);
            return;
        }

        var result = await accounts.SendPasswordResetEmailAsync(EmailBox.Text.Trim());
        Status(
            result.Error ?? "Se houver uma conta para este e-mail, enviamos as instruções de recuperação.",
            result.Error is not null);
    }

    private async void ResendVerification_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => accounts.ResendVerificationEmailAsync(), "Reenviamos o e-mail de confirmação.");

    private async void RefreshVerification_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => accounts.RefreshEmailVerificationAsync());

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        await accounts.LogoutAsync();
        DialogResult = true;
    }

    private async void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPasswordField.Password.Length == 0) { Reject("Confirme sua senha atual para alterar a senha.", CurrentPasswordField); return; }
        if (!AccountPasswordPolicy.IsValid(NewPasswordField.Password))
        {
            Reject($"A nova senha precisa de pelo menos {AccountPasswordPolicy.MinimumLength} caracteres.", NewPasswordField);
            return;
        }
        await RunAsync(
            () => accounts.ChangePasswordAsync(CurrentPasswordField.Password, NewPasswordField.Password),
            "Sua senha foi alterada.");
    }

    private async void ChangeEmail_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPasswordField.Password.Length == 0) { Reject("Confirme sua senha atual para alterar o e-mail.", CurrentPasswordField); return; }
        if (!AccountValidation.IsValidEmail(NewEmailBox.Text)) { Reject("Informe um endereço de e-mail válido.", NewEmailBox); return; }
        await RunAsync(() => accounts.ChangeEmailAsync(CurrentPasswordField.Password, NewEmailBox.Text.Trim()));
    }

    private async void DeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPasswordField.Password.Length == 0) { Reject("Confirme sua senha atual para excluir a conta.", CurrentPasswordField); return; }
        if (System.Windows.MessageBox.Show(
                "Excluir sua conta permanentemente? Seu nome de usuário voltará a ficar disponível para outra pessoa.",
                "Excluir conta",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }
        await RunAsync(() => accounts.DeleteAccountAsync(CurrentPasswordField.Password));
    }

    private async Task<FirebaseAuthResult> RunAsync(Func<Task<FirebaseAuthResult>> action, string? success = null)
    {
        SetBusy(true);
        try
        {
            var result = await action();
            Status(
                result.Error
                    ?? success
                    ?? (result.State == AuthenticationState.EmailVerificationRequired ? "Confirme seu e-mail para continuar." : string.Empty),
                result.Error is not null);
            return result;
        }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        SubmitButton.IsEnabled = SwitchButton.IsEnabled = GoogleButton.IsEnabled = !busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
    }

    private void Terms_Click(object sender, RoutedEventArgs e) => new TermsOfUseWindow { Owner = this }.ShowDialog();

    private void ClearStatus() => StatusPanel.Visibility = Visibility.Collapsed;

    private void Status(string text, bool error)
    {
        if (string.IsNullOrEmpty(text)) { ClearStatus(); return; }

        StatusPanel.Visibility = Visibility.Visible;
        StatusText.Text = text;
        StatusText.SetResourceReference(ForegroundProperty, error ? "RedBrush" : "GreenBrush");
        StatusIcon.Data = (Geometry)FindResource(error ? "IconInfo" : "IconCheck");
        StatusIcon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, error ? "RedBrush" : "GreenBrush");
        StatusPanel.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, error ? "RedBrush" : "GreenBrush");
        StatusPanel.Background = new SolidColorBrush(((SolidColorBrush)FindResource(error ? "RedBrush" : "GreenBrush")).Color) { Opacity = 0.12 };
    }
}
