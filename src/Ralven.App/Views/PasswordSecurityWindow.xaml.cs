using System.Windows;
using System.Windows.Media;
using Ralven.App.Services;

namespace Ralven.App.Views;

public partial class PasswordSecurityWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly IFirebaseAuthService accounts;
    private readonly IGoogleOAuthClient googleOAuth;
    private readonly bool hasPassword;
    private readonly bool hasGoogle;

    public PasswordSecurityWindow(IFirebaseAuthService accounts, IGoogleOAuthClient googleOAuth)
    {
        this.accounts = accounts;
        this.googleOAuth = googleOAuth;
        hasPassword = accounts.Current.User?.HasPassword == true;
        hasGoogle = accounts.Current.User?.HasGoogle == true;
        InitializeComponent();

        TitleBarText.Text = T(hasPassword ? "PasswordSecurity.Reset.Title" : "PasswordSecurity.Create.Title");
        TitleText.Text = TitleBarText.Text;
        SubtitleText.Text = T(hasPassword ? "PasswordSecurity.Reset.Subtitle" : "PasswordSecurity.Create.Subtitle");
        SubmitButton.Content = T(hasPassword ? "PasswordSecurity.Reset.Action" : "PasswordSecurity.Create.Action");
        CurrentPasswordPanel.Visibility = hasPassword ? Visibility.Visible : Visibility.Collapsed;
        GoogleConfirmationPanel.Visibility = hasGoogle ? Visibility.Visible : Visibility.Collapsed;
        GoogleConfirmationText.Text = T(hasPassword
            ? "PasswordSecurity.GoogleFallback"
            : "PasswordSecurity.GoogleConfirmation");
        CurrentMfaPanel.Visibility = accounts.Current.User?.Factors.Any(
            factor => factor.FactorType == FirebaseMfaFactorType.Totp) == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        PasswordPolicyText.Text = F("Account.Password.PolicyMinimum", AccountPasswordPolicy.MinimumLength);
    }

    private async void Submit_Click(object sender, RoutedEventArgs e) => await SubmitAsync();

    private async Task SubmitAsync()
    {
        if (hasPassword && !hasGoogle && CurrentPasswordField.Password.Length == 0)
        {
            Reject(T("PasswordSecurity.Validation.CurrentPasswordRequired"), CurrentPasswordField);
            return;
        }

        if (!AccountPasswordPolicy.IsValid(NewPasswordField.Password))
        {
            Reject(F("PasswordSecurity.Validation.MinimumLength", AccountPasswordPolicy.MinimumLength), NewPasswordField);
            return;
        }

        if (hasPassword && CurrentPasswordField.Password == NewPasswordField.Password)
        {
            Reject(T("PasswordSecurity.Validation.PasswordUnchanged"), NewPasswordField);
            return;
        }

        if (NewPasswordField.Password != ConfirmPasswordField.Password)
        {
            Reject(T("Account.Validation.PasswordsMustMatch"), ConfirmPasswordField);
            return;
        }

        SetBusy(true);
        try
        {
            FirebaseAuthResult result;
            if (hasPassword && CurrentPasswordField.Password.Length > 0)
            {
                result = await accounts.ReauthenticateWithPasswordAsync(CurrentPasswordField.Password);
            }
            else
            {
                if (!googleOAuth.IsConfigured)
                {
                    Status(T("PasswordSecurity.GoogleUnavailable"));
                    return;
                }

                Status(T("PasswordSecurity.GoogleOpening"), error: false);
                var ticket = await googleOAuth.AuthenticateAsync();
                if (ticket.IdToken is null)
                {
                    Status(ticket.Error ?? T("Account.Google.Failed"));
                    return;
                }

                result = await accounts.ReauthenticateWithGoogleAsync(ticket.IdToken);
            }

            result = await CompleteSecondFactorAsync(result);
            if (!result.Succeeded)
            {
                Status(FriendlyError(result.Error));
                return;
            }

            result = hasPassword
                ? await accounts.UpdatePasswordAfterReauthenticationAsync(NewPasswordField.Password)
                : await accounts.CreatePasswordAsync(NewPasswordField.Password);
            if (!result.Succeeded)
            {
                Status(FriendlyError(result.Error));
                return;
            }

            DialogResult = true;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Password_Changed(object? sender, EventArgs e)
    {
        PasswordPolicyText.Text = NewPasswordField.Password.Length >= AccountPasswordPolicy.MinimumLength
            ? T("Account.Password.PolicyMet")
            : F("Account.Password.PolicyRemaining", Math.Max(0, AccountPasswordPolicy.MinimumLength - NewPasswordField.Password.Length), AccountPasswordPolicy.MinimumLength);
        PasswordPolicyText.SetResourceReference(
            ForegroundProperty,
            NewPasswordField.Password.Length >= AccountPasswordPolicy.MinimumLength ? "SuccessBaseBrush" : "TextTertiaryBrush");
    }

    private void Reject(string message, UIElement field)
    {
        Status(message);
        field.Focus();
    }

    private void SetBusy(bool busy)
    {
        CurrentPasswordField.IsEnabled = !busy;
        CurrentMfaCodeBox.IsEnabled = !busy;
        NewPasswordField.IsEnabled = !busy;
        ConfirmPasswordField.IsEnabled = !busy;
        SubmitButton.IsEnabled = !busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
    }

    private async Task<FirebaseAuthResult> CompleteSecondFactorAsync(FirebaseAuthResult result)
    {
        if (result.State != AuthenticationState.MfaChallengeRequired)
        {
            return result;
        }

        var factor = result.MfaChallenge?.Enrollments.FirstOrDefault(
            item => item.FactorType == FirebaseMfaFactorType.Totp);
        var code = CurrentMfaCodeBox.Text.Trim();
        if (factor is null || code.Length != 6 || !code.All(char.IsAsciiDigit))
        {
            return new FirebaseAuthResult(
                AuthenticationState.MfaChallengeRequired,
                accounts.Current.User,
                T("Account.Mfa.InvalidCode"));
        }

        return await accounts.CompleteMfaReauthenticationAsync(factor.Id, code);
    }

    private void Status(string text, bool error = true)
    {
        StatusPanel.Visibility = Visibility.Visible;
        StatusText.Text = text;
        StatusText.SetResourceReference(ForegroundProperty, error ? "DangerBaseBrush" : "TextSecondaryBrush");
        StatusIcon.Data = (Geometry)FindResource(error ? "IconAlertTriangle" : "IconInfo");
        StatusIcon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, error ? "DangerBaseBrush" : "AccentBrightBrush");
        StatusPanel.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, error ? "DangerBorderBrush" : "BorderSubtleBrush");
        StatusPanel.SetResourceReference(BackgroundProperty, error ? "DangerSurfaceBrush" : "Surface2Brush");
    }

    private string FriendlyError(string? error) => error switch
    {
        FirebaseAuthErrorCodes.GoogleAccountMismatch => T("PasswordSecurity.Validation.GoogleAccountMismatch"),
        FirebaseAuthErrorCodes.AccountAlreadyHasPassword => T("PasswordSecurity.Validation.AlreadyHasPassword"),
        FirebaseAuthErrorCodes.CurrentPasswordInvalid => T("PasswordSecurity.Validation.CurrentPasswordInvalid"),
        _ => error ?? T("PasswordSecurity.Validation.Failed"),
    };

    private static string T(string key) => LocalizationService.Current.GetString(key);

    private static string F(string key, params object?[] arguments) => LocalizationService.Current.Format(key, arguments);
}
