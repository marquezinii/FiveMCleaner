using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FiveMCleaner.App.Services;

namespace FiveMCleaner.App.Views;

public partial class AccountWindow : Wpf.Ui.Controls.FluentWindow
{
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MOVE = 0xF010;
    /// <summary>
    /// Idle time after the last keystroke before the username is checked
    /// against the server. Long enough that typing a name end to end costs
    /// one request, short enough to feel immediate.
    /// </summary>
    private static readonly TimeSpan UsernameProbeDelay = TimeSpan.FromMilliseconds(450);

    private readonly IFirebaseAuthService accounts;
    private readonly IAccountProfileService profiles;
    private readonly IGoogleOAuthClient googleOAuth;
    private readonly ILocalizationService localization;
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

    public AccountWindow(
        IFirebaseAuthService accounts,
        IAccountProfileService profiles,
        IGoogleOAuthClient googleOAuth,
        ILocalizationService? localization = null)
    {
        this.accounts = accounts;
        this.profiles = profiles;
        this.googleOAuth = googleOAuth;
        this.localization = localization ?? LocalizationService.Current;
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

    private void Accounts_StateChanged(object? sender, AuthenticationSnapshot state) => _ = Dispatcher.InvokeAsync(() => Render(state));

    /// <summary>Esc cancels, exactly like the X in the title bar.</summary>
    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }

    /// <summary>
    /// Locks the window in place: it opens centered over its owner and stays
    /// there. Blocking <c>WM_SYSCOMMAND</c>/<c>SC_MOVE</c> covers every way
    /// Windows can initiate a move -- dragging the title bar, Alt+Space then
    /// M, double-clicking the system menu icon -- regardless of which one
    /// Wpf.Ui's <c>ui:TitleBar</c> uses internally to drag the window.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(BlockWindowMove);
        }
    }

    private static IntPtr BlockWindowMove(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        handled = IsMoveCommand(msg, wParam);
        return IntPtr.Zero;
    }

    /// <summary>
    /// Extracted so the masking logic (the low nibble of <c>wParam</c> can
    /// carry an extra flag depending on how the move was initiated) is
    /// covered by a plain unit test instead of only being exercisable by
    /// actually dragging a live window.
    /// </summary>
    internal static bool IsMoveCommand(int msg, IntPtr wParam) =>
        msg == WM_SYSCOMMAND && (wParam.ToInt32() & 0xFFF0) == SC_MOVE;

    private void Render(AuthenticationSnapshot state)
    {
        if (state.State == AuthenticationState.SignedIn && !requiresProfileSetup)
        {
            // Account management (senha, e-mail, foto de perfil, excluir
            // conta) now lives em Configurações > Sua conta -- once the
            // account is fully signed in, this window's only job (getting
            // there) is done.
            CloseAfterSignIn();
            return;
        }

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
        LogoutButton.Visibility = Show(hasUser);
        SubmitButton.Visibility = Show(collectingCredentials);
        SwitchButton.Visibility = Show(!hasUser && !requiresProfileSetup);

        SubmitButton.Content = requiresProfileSetup ? T("Account.Actions.FinishRegistration") : registering ? T("Account.Actions.CreateAccount") : T("Account.Actions.SignIn");

        if (requiresProfileSetup)
        {
            TitleText.Text = T("Account.ProfileSetup.Title");
            SubtitleText.Text = T("Account.ProfileSetup.Subtitle");
            // Nothing to recover with when there is no password in this flow.
            ResetPasswordButton.Visibility = Visibility.Collapsed;
        }
        else if (verification)
        {
            TitleText.Text = T("Account.Verification.Title");
            SubtitleText.Text = T("Account.Verification.Subtitle");
            VerificationDetailText.Text = F("Account.Verification.Detail", state.User!.Email);
        }
        else
        {
            ApplyModeCopy();
        }
    }

    /// <summary>
    /// Closing via <see cref="DialogResult"/> requires the window to have
    /// been opened with <see cref="Window.ShowDialog"/> -- true for every
    /// real caller (<c>MainWindow.Account_Click</c>). A harness that built
    /// the window without showing it (as some tests do, to inspect its
    /// visual tree without flashing a real window) would hit
    /// <see cref="InvalidOperationException"/> here; falling back to
    /// <see cref="Window.Close"/> keeps that scenario safe too.
    /// </summary>
    private void CloseAfterSignIn()
    {
        try
        {
            DialogResult = true;
        }
        catch (InvalidOperationException)
        {
            Close();
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
        TitleText.Text = registering ? T("Account.Register.Title") : T("Account.Welcome.Title");
        SubtitleText.Text = registering
            ? T("Account.Register.Subtitle")
            : T("Account.Welcome.Subtitle");
        SubmitButton.Content = registering ? T("Account.Actions.CreateAccount") : T("Account.Actions.SignIn");
        SwitchButton.Content = registering ? T("Account.Switch.ToSignIn") : T("Account.Switch.ToRegister");
        GoogleButtonText.Text = registering ? T("Account.Provider.SignUpWithGoogle") : T("Account.Provider.ContinueWithGoogle");
        ProviderDividerText.Text = registering ? T("Account.Provider.DividerRegister") : T("Account.Provider.DividerSignIn");
        PasswordField.Placeholder = registering ? T("Account.Credentials.PasswordPlaceholderRegister") : T("Account.Credentials.PasswordPlaceholder");
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
            Status(T("Account.Validation.InvalidEmail"), true);
            EmailBox.Focus();
            return;
        }

        if (PasswordField.Password.Length == 0)
        {
            Status(T("Account.Validation.PasswordRequired"), true);
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
        if (!ValidateProfileFields()) return false;
        if (!AccountValidation.IsValidEmail(EmailBox.Text)) { return Reject(T("Account.Validation.InvalidEmail"), EmailBox); }
        if (!AccountPasswordPolicy.IsValid(PasswordField.Password))
        {
            return Reject(T("Account.Validation.PasswordMinLength"), PasswordField);
        }
        if (PasswordField.Password != ConfirmPasswordField.Password)
        {
            return Reject(T("Account.Validation.PasswordsMustMatch"), ConfirmPasswordField);
        }
        if (TermsCheckBox.IsChecked != true)
        {
            Status(T("Account.Validation.TermsRequired"), true);
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

    /// <summary>Nome, sobrenome e nome de usuário — shared by registration (which also collects e-mail/senha) and the post-Google profile step (which only needs these).</summary>
    private bool ValidateProfileFields()
    {
        if (!AccountValidation.IsValidPersonName(FirstNameBox.Text)) { return Reject(T("Account.Validation.FirstNameRequired"), FirstNameBox); }
        if (!AccountValidation.IsValidPersonName(LastNameBox.Text)) { return Reject(T("Account.Validation.LastNameRequired"), LastNameBox); }
        if (!AccountValidation.IsValidUsername(UsernameBox.Text))
        {
            return Reject(T("Account.Validation.UsernameFormat"), UsernameBox);
        }
        return true;
    }

    private async Task SubmitProfileAsync()
    {
        if (!ValidateProfileFields()) return;

        SetBusy(true);
        try { await SaveProfileAsync(); } finally { SetBusy(false); }
    }

    private async Task SaveProfileAsync()
    {
        var token = await accounts.GetIdTokenAsync();
        if (token is null)
        {
            requiresProfileSetup = true;
            Status(T("Account.Session.Expired"), true);
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
            Status(result.Message ?? T("Account.Profile.SaveFailed"), true);
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
            Status(T("Account.Google.SigningIn"), false);
            var ticket = await googleOAuth.AuthenticateAsync();
            if (ticket.IdToken is null) { Status(ticket.Error ?? T("Account.Google.Failed"), true); return; }

            var federated = await accounts.SignInWithGoogleAsync(ticket.IdToken, KeepSignedInBox.IsChecked == true);
            if (!federated.Result.Succeeded)
            {
                Status(federated.Result.Error ?? T("Account.Google.Failed"), true);
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
            CloseAfterSignIn();
            return;
        }

        if (existing.Outcome == AccountProfileFetchOutcome.Failed)
        {
            // The account is signed in and valid; only the profile lookup
            // failed (offline, Worker down). Forcing the profile step here
            // would risk a duplicate row for a name the user already owns.
            CloseAfterSignIn();
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
        UsernameStatusText.Text = T("Account.Username.Checking");
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
            ? T("Account.Username.Available")
            : T("Account.Username.Taken");
    }

    // ===================== Senha =====================

    private void Password_Changed(object? sender, EventArgs e) => UpdatePasswordFeedback();

    private void UpdatePasswordFeedback()
    {
        var length = PasswordField.Password.Length;
        PasswordStrengthBar.Value = Math.Min(length, AccountPasswordPolicy.MinimumLength);
        PasswordPolicyText.Text = length == 0
            ? F("Account.Password.PolicyMinimum", AccountPasswordPolicy.MinimumLength)
            : length < AccountPasswordPolicy.MinimumLength
                ? F("Account.Password.PolicyRemaining", AccountPasswordPolicy.MinimumLength - length, AccountPasswordPolicy.MinimumLength)
                : T("Account.Password.PolicyMet");
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
        ConfirmStatusText.Text = matches ? T("Account.Password.ConfirmMatch") : T("Account.Password.ConfirmMismatch");
    }

    // ===================== Demais ações =====================

    private async void ResetPassword_Click(object sender, RoutedEventArgs e)
    {
        if (!AccountValidation.IsValidEmail(EmailBox.Text))
        {
            Reject(T("Account.Validation.EmailRequiredForReset"), EmailBox);
            return;
        }

        SetBusy(true);
        try
        {
            var result = await accounts.SendPasswordResetEmailAsync(EmailBox.Text.Trim());
            Status(
                result.Error ?? T("Account.PasswordReset.Sent"),
                result.Error is not null);
        }
        finally { SetBusy(false); }
    }

    private async void ResendVerification_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => accounts.ResendVerificationEmailAsync(), T("Account.Verification.ResentConfirmation"));

    private async void RefreshVerification_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => accounts.RefreshEmailVerificationAsync());

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        await accounts.LogoutAsync();
        CloseAfterSignIn();
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
                    ?? (result.State == AuthenticationState.EmailVerificationRequired ? T("Account.Verification.PleaseConfirm") : string.Empty),
                result.Error is not null);
            return result;
        }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        SubmitButton.IsEnabled = SwitchButton.IsEnabled = GoogleButton.IsEnabled = !busy;
        ResendVerificationButton.IsEnabled = !busy;
        RefreshVerificationButton.IsEnabled = !busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
    }

    private void Terms_Click(object sender, RoutedEventArgs e) => new TermsOfUseWindow { Owner = this }.ShowDialog();

    private string T(string key) => localization.GetString(key);

    private string F(string key, params object?[] arguments) => localization.Format(key, arguments);

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
        StatusPanel.SetResourceReference(BackgroundProperty, error ? "AccountStatusErrorBackgroundBrush" : "AccountStatusSuccessBackgroundBrush");
    }
}
