using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FiveMCleaner.App.Services;
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
    /// <summary>Segoe MDL2 Assets "contact" glyph -- the header's signed-out fallback, matching the button's original default.</summary>
    private const string SignedOutGlyph = "";

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

        var user = accountService.Current.User;
        AccountSettingsUnavailablePanel.Visibility = Visibility.Collapsed;
        AccountSettingsSignedOutPanel.Visibility = user is null ? Visibility.Visible : Visibility.Collapsed;
        AccountSettingsSignedInPanel.Visibility = user is null ? Visibility.Collapsed : Visibility.Visible;

        if (user is null)
        {
            ApplyAvatar(null, AccountAvatarEllipse, AccountInitials, SignedOutGlyph, isGlyph: true);
            ApplyAvatar(null, AccountSettingsAvatarEllipse, AccountSettingsInitials, string.Empty, isGlyph: false);
            return;
        }

        AccountSettingsEmailText.Text = user.Email;
        RemovePhotoButton.Visibility = File.Exists(avatarStore.PathFor(user.Uid)) ? Visibility.Visible : Visibility.Collapsed;

        var avatar = avatarStore.TryLoad(user.Uid);
        ApplyAvatar(avatar, AccountAvatarEllipse, AccountInitials, user.Initials, isGlyph: false);
        ApplyAvatar(avatar, AccountSettingsAvatarEllipse, AccountSettingsInitials, user.Initials, isGlyph: false);
    }

    /// <summary>Sets <paramref name="username"/> on the Settings card once <see cref="SyncAccountFirstNameAsync"/> has read the profile.</summary>
    private void ApplyAccountSettingsUsername(string? username) =>
        AccountSettingsUsernameText.Text = string.IsNullOrWhiteSpace(username) ? string.Empty : $"@{username}";

    private static void ApplyAvatar(BitmapImage? avatar, System.Windows.Shapes.Ellipse ellipse, TextBlock fallback, string fallbackText, bool isGlyph)
    {
        if (avatar is null)
        {
            ellipse.Visibility = Visibility.Collapsed;
            fallback.Visibility = Visibility.Visible;
            fallback.Text = fallbackText;
            fallback.FontFamily = isGlyph ? new System.Windows.Media.FontFamily("Segoe MDL2 Assets") : fallback.FontFamily;
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

    private async void AccountSettingsChangePassword_Click(object sender, RoutedEventArgs e)
    {
        if (accountService is null)
        {
            return;
        }

        if (AccountSettingsCurrentPasswordField.Password.Length == 0)
        {
            AccountSettingsStatus("Confirme sua senha atual para alterar a senha.", error: true);
            AccountSettingsCurrentPasswordField.Focus();
            return;
        }

        if (!AccountPasswordPolicy.IsValid(AccountSettingsNewPasswordField.Password))
        {
            AccountSettingsStatus($"A nova senha precisa de pelo menos {AccountPasswordPolicy.MinimumLength} caracteres.", error: true);
            AccountSettingsNewPasswordField.Focus();
            return;
        }

        await RunAccountSettingsActionAsync(
            () => accountService.ChangePasswordAsync(AccountSettingsCurrentPasswordField.Password, AccountSettingsNewPasswordField.Password),
            "Sua senha foi alterada.");
        AccountSettingsNewPasswordField.Clear();
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
        var result = await RunAccountSettingsActionAsync(() => accountService.DeleteAccountAsync(AccountSettingsCurrentPasswordField.Password));
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

    private async Task<FirebaseAuthResult> RunAccountSettingsActionAsync(Func<Task<FirebaseAuthResult>> action, string? success = null)
    {
        SetAccountSettingsBusy(true);
        try
        {
            var result = await action();
            AccountSettingsStatus(result.Error ?? success ?? string.Empty, error: result.Error is not null);
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
        AccountSettingsStatusText.SetResourceReference(ForegroundProperty, error ? "RedBrush" : "GreenBrush");
        AccountSettingsStatusIcon.Data = (Geometry)FindResource(error ? "IconInfo" : "IconCheck");
        AccountSettingsStatusIcon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, error ? "RedBrush" : "GreenBrush");
        AccountSettingsStatusPanel.SetResourceReference(BorderBrushProperty, error ? "RedBrush" : "GreenBrush");
        AccountSettingsStatusPanel.Background = new SolidColorBrush(((SolidColorBrush)FindResource(error ? "RedBrush" : "GreenBrush")).Color) { Opacity = 0.12 };
    }
}
