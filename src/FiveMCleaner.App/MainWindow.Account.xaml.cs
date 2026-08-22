using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FiveMCleaner.App.Services;
using FiveMCleaner.App.Views;
namespace FiveMCleaner.App;

/// <summary>
/// The "Sua conta" section of the Settings page: profile photo, password,
/// e-mail and account deletion. This used to live inside a popup
/// (<c>AccountWindow</c>'s now-removed management panel) that reappeared
/// every time the signed-in user clicked their name in the header; it is now
/// a permanent card in Configurações, consistent with every other setting.
/// <c>AccountWindow</c> itself is used only for the sign-in/registration
/// journey and closes itself the moment the account becomes fully signed in
/// (see its <c>Render</c>/<c>CloseAfterSignIn</c>).
/// </summary>
public partial class MainWindow
{
    private readonly AccountAvatarStore avatarStore = new();

    private void OpenAccountFromSettings_Click(object sender, RoutedEventArgs e) => OpenAccountWindow();

    /// <summary>
    /// Reflects the current sign-in state into the Settings card: which of
    /// the three panels (unavailable / signed out / signed in) is shown, the
    /// e-mail/username readout, and the avatar in both the card and the
    /// header button. Called on every account state change and whenever the
    /// user navigates into Settings.
    /// </summary>
    private void RefreshAccountSettingsCard()
    {
        if (accountService is null)
        {
            AccountSettingsUnavailablePanel.Visibility = Visibility.Visible;
            AccountSettingsSignedOutPanel.Visibility = Visibility.Collapsed;
            AccountSettingsSignedInPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var user = accountService.Current.State == AuthenticationState.SignedIn
            ? accountService.Current.User
            : null;
        AccountSettingsUnavailablePanel.Visibility = Visibility.Collapsed;
        AccountSettingsSignedOutPanel.Visibility = user is null ? Visibility.Visible : Visibility.Collapsed;
        AccountSettingsSignedInPanel.Visibility = user is null ? Visibility.Collapsed : Visibility.Visible;

        // Signed in, the header button is just the avatar/initials -- the
        // "Entrar / Cadastre-se" prompt only makes sense while signed out.
        AccountLabel.Visibility = user is null ? Visibility.Visible : Visibility.Collapsed;

        if (user is null)
        {
            ApplyAvatar(null, AccountAvatarEllipse, AccountFallbackIcon);
            ApplyAvatar(null, AccountSettingsAvatarEllipse, AccountSettingsFallbackIcon);
            return;
        }

        AccountSettingsEmailText.Text = user.Email;
        var hasPassword = user.HasPassword;
        AccountSettingsPasswordValue.Text = hasPassword
            ? "••••••••••"
            : LocalizationService.Current.GetString("Settings.Account.PasswordNotConfigured");
        var passwordAction = LocalizationService.Current.GetString(
            hasPassword ? "Settings.Account.ResetPasswordTooltip" : "Settings.Account.CreatePasswordTooltip");
        AccountSettingsPasswordButton.ToolTip = passwordAction;
        AutomationProperties.SetName(AccountSettingsPasswordButton, passwordAction);
        AccountSettingsCurrentPasswordPanel.Visibility = hasPassword ? Visibility.Visible : Visibility.Collapsed;
        AccountSettingsPasswordRequiredHint.Visibility = hasPassword ? Visibility.Collapsed : Visibility.Visible;
        AccountSettingsChangeEmailButton.IsEnabled = hasPassword;
        AccountSettingsDeleteAccountButton.IsEnabled = hasPassword;
        ToolTipService.SetShowOnDisabled(AccountSettingsChangeEmailButton, !hasPassword);
        ToolTipService.SetShowOnDisabled(AccountSettingsDeleteAccountButton, !hasPassword);
        var passwordRequired = LocalizationService.Current.GetString("Settings.Account.PasswordRequiredForSensitiveActions");
        AccountSettingsChangeEmailButton.ToolTip = hasPassword ? null : passwordRequired;
        AccountSettingsDeleteAccountButton.ToolTip = hasPassword ? null : passwordRequired;
        RemovePhotoButton.Visibility = File.Exists(avatarStore.PathFor(user.Uid)) ? Visibility.Visible : Visibility.Collapsed;

        var avatar = avatarStore.TryLoad(user.Uid);
        ApplyAvatar(avatar, AccountAvatarEllipse, AccountFallbackIcon);
        ApplyAvatar(avatar, AccountSettingsAvatarEllipse, AccountSettingsFallbackIcon);
    }

    /// <summary>Sets <paramref name="username"/> on the Settings card once <see cref="SyncAccountFirstNameAsync"/> has read the profile.</summary>
    private void ApplyAccountSettingsUsername(string? username) =>
        AccountSettingsUsernameText.Text = string.IsNullOrWhiteSpace(username) ? string.Empty : $"@{username}";

    private static void ApplyAvatar(BitmapImage? avatar, System.Windows.Shapes.Ellipse ellipse, Wpf.Ui.Controls.SymbolIcon fallback)
    {
        if (avatar is null)
        {
            ellipse.Fill = null;
            ellipse.Visibility = Visibility.Collapsed;
            fallback.Visibility = Visibility.Visible;
            return;
        }

        ellipse.Fill = new ImageBrush(avatar) { Stretch = Stretch.UniformToFill };
        ellipse.Visibility = Visibility.Visible;
        fallback.Visibility = Visibility.Collapsed;
    }

    private void ChangePhoto_Click(object sender, RoutedEventArgs e)
    {
        var user = accountService?.Current.User;
        if (user is null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Escolher foto de perfil",
            Filter = "Imagens (*.jpg;*.jpeg;*.png;*.bmp;*.webp)|*.jpg;*.jpeg;*.png;*.bmp;*.webp",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (avatarStore.TrySave(user.Uid, dialog.FileName))
        {
            AccountSettingsStatus("Foto de perfil atualizada.", error: false);
            RefreshAccountSettingsCard();
        }
        else
        {
            AccountSettingsStatus("Não foi possível usar essa imagem. Verifique se o arquivo é uma foto válida de até 8 MB.", error: true);
        }
    }

    private void RemovePhoto_Click(object sender, RoutedEventArgs e)
    {
        var user = accountService?.Current.User;
        if (user is null)
        {
            return;
        }

        avatarStore.Delete(user.Uid);
        AccountSettingsStatus("Foto de perfil removida.", error: false);
        RefreshAccountSettingsCard();
    }

    private void AccountSettingsPassword_Click(object sender, RoutedEventArgs e)
    {
        if (accountService is null)
        {
            return;
        }

        var dialog = new PasswordSecurityWindow(accountService, googleOAuth) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            AccountSettingsStatus(
                LocalizationService.Current.GetString("PasswordSecurity.Success"),
                error: false);
            RefreshAccountSettingsCard();
        }
    }

    private async void AccountSettingsChangeEmail_Click(object sender, RoutedEventArgs e)
    {
        if (accountService is null)
        {
            return;
        }

        if (AccountSettingsCurrentPasswordField.Password.Length == 0)
        {
            AccountSettingsStatus("Confirme sua senha atual para alterar o e-mail.", error: true);
            AccountSettingsCurrentPasswordField.Focus();
            return;
        }

        if (!AccountValidation.IsValidEmail(AccountSettingsNewEmailBox.Text))
        {
            AccountSettingsStatus("Informe um endereço de e-mail válido.", error: true);
            AccountSettingsNewEmailBox.Focus();
            return;
        }

        await RunAccountSettingsActionAsync(
            () => accountService.ChangeEmailAsync(AccountSettingsCurrentPasswordField.Password, AccountSettingsNewEmailBox.Text.Trim()));
    }

    private async void AccountSettingsDeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        if (accountService is null)
        {
            return;
        }

        if (AccountSettingsCurrentPasswordField.Password.Length == 0)
        {
            AccountSettingsStatus("Confirme sua senha atual para excluir a conta.", error: true);
            AccountSettingsCurrentPasswordField.Focus();
            return;
        }

        if (System.Windows.MessageBox.Show(
                "Excluir sua conta permanentemente? Seu nome de usuário voltará a ficar disponível para outra pessoa.",
                "Excluir conta",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var user = accountService.Current.User;
        var result = await RunAccountSettingsActionAsync(
            () => accountService.DeleteAccountAsync(AccountSettingsCurrentPasswordField.Password),
            errorKey: "Account.Profile.DeleteFailed");
        if (result.Succeeded && user is not null)
        {
            // The account is gone; a leftover local photo would just be an
            // orphaned file with nothing to attach it to.
            avatarStore.Delete(user.Uid);
        }
    }

    private async void AccountSettingsLogout_Click(object sender, RoutedEventArgs e)
    {
        if (accountService is null)
        {
            return;
        }

        await accountService.LogoutAsync();
        AccountSettingsCurrentPasswordField.Clear();
    }

    private async Task<FirebaseAuthResult> RunAccountSettingsActionAsync(
        Func<Task<FirebaseAuthResult>> action,
        string? success = null,
        string? errorKey = null)
    {
        SetAccountSettingsBusy(true);
        try
        {
            var result = await action();
            var error = result.Error == FirebaseAuthService.ProfileDeletionFailedError && errorKey is not null
                ? LocalizationService.Current.GetString(errorKey)
                : result.Error;
            AccountSettingsStatus(error ?? success ?? string.Empty, error: error is not null);
            return result;
        }
        finally
        {
            SetAccountSettingsBusy(false);
        }
    }

    private void SetAccountSettingsBusy(bool busy)
    {
        ChangePhotoButton.IsEnabled = !busy;
        RemovePhotoButton.IsEnabled = !busy;
        AccountSettingsPasswordButton.IsEnabled = !busy;
        var hasPassword = accountService?.Current.User?.HasPassword == true;
        AccountSettingsChangeEmailButton.IsEnabled = !busy && hasPassword;
        AccountSettingsDeleteAccountButton.IsEnabled = !busy && hasPassword;
        AccountSettingsLogoutButton.IsEnabled = !busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
    }

    private void AccountSettingsStatus(string text, bool error)
    {
        if (string.IsNullOrEmpty(text))
        {
            AccountSettingsStatusPanel.Visibility = Visibility.Collapsed;
            return;
        }

        AccountSettingsStatusPanel.Visibility = Visibility.Visible;
        AccountSettingsStatusText.Text = text;
        AccountSettingsStatusText.SetResourceReference(ForegroundProperty, error ? "DangerBaseBrush" : "SuccessBaseBrush");
        AccountSettingsStatusIcon.Data = (Geometry)FindResource(error ? "IconAlertTriangle" : "IconCheck");
        AccountSettingsStatusIcon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, error ? "DangerBaseBrush" : "SuccessBaseBrush");
        AccountSettingsStatusPanel.SetResourceReference(BorderBrushProperty, error ? "DangerBorderBrush" : "SuccessBorderBrush");
        AccountSettingsStatusPanel.SetResourceReference(BackgroundProperty, error ? "DangerSurfaceBrush" : "SuccessSurfaceBrush");
    }

    private IFirebaseAuthService? CreateAccountService(
        bool demoMode,
        RemoteServicesOptions options,
        IAccountProfileService profiles)
    {
        if (demoMode
            || !FirebaseAuthConfiguration.TryGetApiKey(options.FirebaseApiKey, out var firebaseApiKey))
        {
            return null;
        }

        var service = new FirebaseAuthService(firebaseApiKey, profiles);
        service.StateChanged += (_, _) => Dispatcher.Invoke(UpdateAccountButton);
        return service;
    }

    private async Task RestoreAccountSessionQuietlyAsync()
    {
        try
        {
            await accountService!.RestoreSessionAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
        }
    }

    private void Account_Click(object sender, RoutedEventArgs e)
    {
        if (accountService is null)
        {
            System.Windows.MessageBox.Show(LocalizationService.Current.GetString("Settings.Account.Unavailable"), LocalizationService.Current.GetString("Settings.Account.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (accountService.Current.State == AuthenticationState.SignedIn)
        {
            // Já logado: o clique no cabeçalho leva direto para
            // Configurações > Sua conta, em vez de reabrir a janela de
            // entrar/cadastrar (que agora só cuida de autenticar, não de
            // gerenciar a conta -- ver AccountWindow.CloseAfterSignIn).
            ActivateNavItem(SettingsNav);
            Navigate(SettingsPage);
            AccountSettingsCard.BringIntoView();
            return;
        }

        OpenAccountWindow();
    }

    private void OpenAccountWindow()
    {
        var dialog = new AccountWindow(accountService!, profileService, googleOAuth) { Owner = this };
        if (dialog.ShowDialog() == true) UpdateAccountButton();
    }

    private void UpdateAccountButton()
    {
        var profile = accountService?.Current.User;
        AccountLabel.Text = profile?.DisplayName ?? LocalizationService.Current.GetString("Account.SignInButton");
        AccountButton.ToolTip = profile is null ? LocalizationService.Current.GetString("Account.SignInTooltip") : LocalizationService.Current.GetString("Account.ViewTooltip");
        // Also sets AccountInitials/avatar for both the header and the
        // Settings card, so a direct assignment here would just be
        // immediately overwritten.
        RefreshAccountSettingsCard();
    }

    private async void AccountService_StateChanged(object? sender, AuthenticationSnapshot snapshot)
    {
        Dispatcher.Invoke(UpdateAccountButton);
        if (snapshot.State != AuthenticationState.SignedIn || snapshot.User is null)
        {
            Dispatcher.Invoke(() =>
            {
                viewModel.SetAccountFirstName(null);
                ApplyAccountSettingsUsername(null);
            });
            return;
        }

        await SyncAccountFirstNameAsync();
    }

    /// <summary>
    /// Reads the caller's own first name for the Overview greeting. Firebase
    /// Authentication REST never stores it, so it only exists in the
    /// Worker's profile table; this is why login and quiet session restore
    /// both need a read call instead of getting it for free off the token.
    /// </summary>
    private async Task SyncAccountFirstNameAsync()
    {
        if (accountService is null)
        {
            return;
        }

        try
        {
            var idToken = await accountService.GetIdTokenAsync().ConfigureAwait(false);
            if (idToken is null)
            {
                return;
            }

            var result = await profileService.FetchAsync(idToken).ConfigureAwait(false);
            if (result.Outcome == AccountProfileFetchOutcome.Found)
            {
                Dispatcher.Invoke(() =>
                {
                    viewModel.SetAccountFirstName(result.FirstName);
                    ApplyAccountSettingsUsername(result.Username);
                });
            }
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            // Sem nome não é um estado de erro visível: a saudação simplesmente
            // fica sem o nome até a próxima sincronização bem-sucedida.
        }
    }
}
