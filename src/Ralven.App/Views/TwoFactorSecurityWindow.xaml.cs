using System.Windows;
using Ralven.App.Controls;
using Ralven.App.Services;

namespace Ralven.App.Views;

public partial class TwoFactorSecurityWindow : DialogWindow
{
    private readonly IFirebaseAuthService accounts;
    private readonly IAccountSecurityService security;
    private readonly IGoogleOAuthClient googleOAuth;
    private string? enrollmentSession;

    public TwoFactorSecurityWindow(
        IFirebaseAuthService accounts,
        IAccountSecurityService security,
        IGoogleOAuthClient googleOAuth)
    {
        this.accounts = accounts;
        this.security = security;
        this.googleOAuth = googleOAuth;
        InitializeComponent();
        Render();
    }

    private FirebaseMfaEnrollment? TotpFactor => accounts.Current.User?.Factors
        .FirstOrDefault(factor => factor.FactorType == FirebaseMfaFactorType.Totp);

    private void Render()
    {
        var user = accounts.Current.User;
        var enabled = TotpFactor is not null;
        PasswordConfirmationPanel.Visibility = user?.HasPassword == true ? Visibility.Visible : Visibility.Collapsed;
        GoogleConfirmationText.Visibility = user?.HasGoogle == true ? Visibility.Visible : Visibility.Collapsed;
        GoogleConfirmationText.Text = T(user is { HasPassword: true, HasGoogle: true }
            ? "PasswordSecurity.GoogleFallback"
            : "TwoFactor.GoogleConfirmation");
        CurrentMfaPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        DisabledActionsPanel.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        EnabledActionsPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task<bool> ConfirmIdentityAsync(bool requireCurrentSecondFactor)
    {
        var user = accounts.Current.User;
        if (user is null)
        {
            Status("TwoFactor.Error.Session");
            return false;
        }

        FirebaseAuthResult result;
        if (user.HasPassword && (CurrentPasswordField.Password.Length > 0 || !user.HasGoogle))
        {
            if (CurrentPasswordField.Password.Length == 0)
            {
                Status("PasswordSecurity.Validation.CurrentPasswordRequired");
                CurrentPasswordField.Focus();
                return false;
            }
            result = await accounts.ReauthenticateWithPasswordAsync(CurrentPasswordField.Password);
        }
        else
        {
            if (!googleOAuth.IsConfigured)
            {
                Status("PasswordSecurity.GoogleUnavailable");
                return false;
            }
            var ticket = await googleOAuth.AuthenticateAsync();
            if (ticket.IdToken is null)
            {
                StatusTextValue(ticket.Error ?? T("Account.Google.Failed"));
                return false;
            }
            result = await accounts.ReauthenticateWithGoogleAsync(ticket.IdToken);
        }

        if (result.State == AuthenticationState.MfaChallengeRequired || requireCurrentSecondFactor)
        {
            var factor = result.MfaChallenge?.Enrollments.FirstOrDefault(item => item.FactorType == FirebaseMfaFactorType.Totp)
                ?? TotpFactor;
            var code = CurrentMfaCodeBox.Text.Trim();
            if (factor is null || !ValidCode(code))
            {
                Status("Account.Mfa.InvalidCode");
                CurrentMfaCodeBox.Focus();
                return false;
            }
            result = await accounts.CompleteMfaReauthenticationAsync(factor.Id, code);
        }

        if (!result.Succeeded)
        {
            StatusTextValue(result.Error ?? T("Account.Error.ReauthenticationRequired"));
            return false;
        }

        CurrentPasswordField.Clear();
        CurrentMfaCodeBox.Clear();
        return true;
    }

    private async void Enable_Click(object sender, RoutedEventArgs e)
    {
        await BusyAsync(async () =>
        {
            if (!await ConfirmIdentityAsync(requireCurrentSecondFactor: false)) return;
            var started = await accounts.StartTotpEnrollmentAsync();
            if (!started.Succeeded)
            {
                StatusTextValue(started.Error ?? T("TwoFactor.Error.Unavailable"));
                return;
            }

            enrollmentSession = started.SessionInfo;
            SharedSecretBox.Text = started.SharedSecretKey;
            EnrollmentPanel.Visibility = Visibility.Visible;
            RecoveryCodesPanel.Visibility = Visibility.Collapsed;
            EnrollmentCodeBox.Focus();
        });
    }

    private async void ConfirmEnrollment_Click(object sender, RoutedEventArgs e)
    {
        if (enrollmentSession is null || !ValidCode(EnrollmentCodeBox.Text.Trim()))
        {
            Status("Account.Mfa.InvalidCode");
            return;
        }

        await BusyAsync(async () =>
        {
            var result = await accounts.FinalizeTotpEnrollmentAsync(enrollmentSession, EnrollmentCodeBox.Text.Trim(), T("TwoFactor.FactorName"));
            var factor = result.User?.Factors.FirstOrDefault(item => item.FactorType == FirebaseMfaFactorType.Totp);
            if (!result.Succeeded || factor is null)
            {
                StatusTextValue(result.Error ?? T("TwoFactor.Error.Unavailable"));
                return;
            }

            var token = await accounts.GetIdTokenAsync();
            var codes = token is null
                ? new RecoveryCodesResult(RecoveryCodesOutcome.Unavailable, [])
                : await security.GenerateRecoveryCodesAsync(token, factor.Id);
            if (!codes.Succeeded)
            {
                var rollback = await accounts.WithdrawMfaEnrollmentAsync(factor.Id);
                Status(rollback.Succeeded
                    ? "TwoFactor.Error.RecoveryCodesRequired"
                    : "TwoFactor.Error.RecoveryCodesRollbackFailed");
                enrollmentSession = null;
                EnrollmentPanel.Visibility = Visibility.Collapsed;
                Render();
                return;
            }

            ShowRecoveryCodes(codes.RecoveryCodes);
            enrollmentSession = null;
            EnrollmentPanel.Visibility = Visibility.Collapsed;
            Status("TwoFactor.Enabled", error: false);
            Render();
        });
    }

    private async void Regenerate_Click(object sender, RoutedEventArgs e)
    {
        var factor = TotpFactor;
        if (factor is null) return;
        await BusyAsync(async () =>
        {
            if (!await ConfirmIdentityAsync(requireCurrentSecondFactor: true)) return;
            var token = await accounts.GetIdTokenAsync();
            var result = token is null
                ? new RecoveryCodesResult(RecoveryCodesOutcome.Unavailable, [])
                : await security.GenerateRecoveryCodesAsync(token, factor.Id);
            if (!result.Succeeded)
            {
                Status("TwoFactor.Error.Unavailable");
                return;
            }
            ShowRecoveryCodes(result.RecoveryCodes);
            Status("TwoFactor.RecoveryCodes.Regenerated", error: false);
        });
    }

    private async void Disable_Click(object sender, RoutedEventArgs e)
    {
        var factor = TotpFactor;
        if (factor is null) return;
        if (OptimizationConfirmationWindow.Confirm(this, T("TwoFactor.DisableConfirmation"), T("TwoFactor.Disable"), T("TwoFactor.Disable")) != true) return;

        await BusyAsync(async () =>
        {
            if (!await ConfirmIdentityAsync(requireCurrentSecondFactor: true)) return;
            var result = await accounts.WithdrawMfaEnrollmentAsync(factor.Id);
            if (!result.Succeeded)
            {
                StatusTextValue(result.Error ?? T("TwoFactor.Error.Unavailable"));
                return;
            }
            var token = await accounts.GetIdTokenAsync();
            if (token is not null) await security.DeleteRecoveryCodesAsync(token, factor.Id);
            RecoveryCodesPanel.Visibility = Visibility.Collapsed;
            Status("TwoFactor.Disabled", error: false);
            Render();
        });
    }

    private void ShowRecoveryCodes(IReadOnlyList<string> codes)
    {
        RecoveryCodesBox.Text = string.Join(Environment.NewLine, codes);
        RecoveryCodesPanel.Visibility = Visibility.Visible;
    }

    private void CopySecret_Click(object sender, RoutedEventArgs e) => Copy(SharedSecretBox.Text);
    private void CopyCodes_Click(object sender, RoutedEventArgs e) => Copy(RecoveryCodesBox.Text);

    private void Copy(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        try
        {
            System.Windows.Clipboard.SetText(value);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            Status("TwoFactor.Error.CopyFailed");
        }
    }

    private async Task BusyAsync(Func<Task> action)
    {
        SetBusy(true);
        try { await action(); }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        EnableButton.IsEnabled = RegenerateButton.IsEnabled = DisableButton.IsEnabled = ConfirmEnrollmentButton.IsEnabled = !busy;
        CurrentPasswordField.IsEnabled = CurrentMfaCodeBox.IsEnabled = EnrollmentCodeBox.IsEnabled = !busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
    }

    private void Status(string key, bool error = true) => StatusTextValue(T(key), error);

    private void StatusTextValue(string value, bool error = true)
    {
        StatusPanel.Visibility = Visibility.Visible;
        StatusText.Text = value;
        StatusText.SetResourceReference(ForegroundProperty, error ? "DangerBaseBrush" : "SuccessBaseBrush");
    }

    internal static bool ValidCode(string value) => value.Length == 6 && value.All(char.IsAsciiDigit);
    private static string T(string key) => LocalizationService.Current.GetString(key);
}
