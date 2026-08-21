using System.Windows;
using System.Windows.Controls;
using FiveMCleaner.App.Services;
using FiveMCleaner.App.Views;
using Xunit;

namespace FiveMCleaner.Tests.App;

/// <summary>
/// Builds the real <see cref="AccountWindow"/> on an STA thread with the
/// application's own resource dictionaries loaded.
///
/// This is the only check that actually parses the window's XAML: a missing
/// StaticResource, a broken template or a renamed brush compiles fine and
/// only throws when a user opens the account window. Everything asserted
/// here is behaviour the user asked for by name — the eye toggle, the
/// "Esqueci minha senha" link appearing only after a failed sign-in, the
/// repeat-password check — so a regression fails the build instead of
/// shipping.
/// </summary>
public sealed class AccountWindowTests
{
    [Fact]
    public void Window_BuildsAndStartsInSignInMode()
    {
        RunOnUiThread(window =>
        {
            Assert.Equal(LocalizationService.Current["Account.Welcome.Title"], Text(window, "TitleText"));
            Assert.Equal(LocalizationService.Current["Account.Actions.SignIn"], Get<Button>(window, "SubmitButton").Content);

            // Cadastro-only surfaces stay hidden while signing in.
            Assert.Equal(Visibility.Collapsed, Get<UIElement>(window, "ConfirmPanel").Visibility);
            Assert.Equal(Visibility.Collapsed, Get<UIElement>(window, "TermsPanel").Visibility);
            Assert.Equal(Visibility.Collapsed, Get<UIElement>(window, "ProfileFieldsPanel").Visibility);
        });
    }

    [Fact]
    public void ForgotPassword_IsHiddenUntilASignInAttemptFails()
    {
        RunOnUiThread(window =>
        {
            var link = Get<Button>(window, "ResetPasswordButton");
            Assert.Equal(Visibility.Collapsed, link.Visibility);

            Get<TextBox>(window, "EmailBox").Text = "person@example.com";

            // An empty password never reaches Firebase, so it is not a wrong
            // password and must not offer recovery yet.
            Invoke(window, "SubmitSignInAsync");
            Assert.Equal(Visibility.Collapsed, link.Visibility);

            SetPassword(Get<FiveMCleaner.App.Controls.RevealPasswordBox>(window, "PasswordField"), "senhaerrada123");
            Invoke(window, "SubmitSignInAsync");

            Assert.Equal(Visibility.Visible, link.Visibility);
        });
    }

    [Fact]
    public void ForgotPassword_NeverAppearsInTheRegistrationForm()
    {
        RunOnUiThread(window =>
        {
            var link = Get<Button>(window, "ResetPasswordButton");

            // Fail a sign-in first, so the link is showing...
            Get<TextBox>(window, "EmailBox").Text = "person@example.com";
            SetPassword(Get<FiveMCleaner.App.Controls.RevealPasswordBox>(window, "PasswordField"), "senhaerrada123");
            Invoke(window, "SubmitSignInAsync");
            Assert.Equal(Visibility.Visible, link.Visibility);

            // ...then switch to "Criar conta": there is no password to
            // recover yet, so it must go away again.
            Get<Button>(window, "SwitchButton").RaiseEvent(new RoutedEventArgs(ButtonBase_ClickEvent));

            Assert.Equal(Visibility.Collapsed, link.Visibility);
            Assert.Equal(Visibility.Visible, Get<UIElement>(window, "ConfirmPanel").Visibility);
            Assert.Equal(Visibility.Visible, Get<UIElement>(window, "ProfileFieldsPanel").Visibility);
            Assert.Equal(LocalizationService.Current["Account.Register.Title"], Text(window, "TitleText"));
        });
    }

    [Fact]
    public void RepeatPassword_ReportsWhetherTheTwoEntriesMatch()
    {
        RunOnUiThread(window =>
        {
            Get<Button>(window, "SwitchButton").RaiseEvent(new RoutedEventArgs(ButtonBase_ClickEvent));

            var password = Get<FiveMCleaner.App.Controls.RevealPasswordBox>(window, "PasswordField");
            var confirm = Get<FiveMCleaner.App.Controls.RevealPasswordBox>(window, "ConfirmPasswordField");
            var status = Get<TextBlock>(window, "ConfirmStatusText");
            var statusPanel = Get<UIElement>(window, "ConfirmStatusPanel");

            SetPassword(password, "senhaseguraaqui");
            Assert.Equal(Visibility.Collapsed, statusPanel.Visibility);

            SetPassword(confirm, "senhadiferente");
            Assert.Equal(Visibility.Visible, statusPanel.Visibility);
            Assert.Equal(LocalizationService.Current["Account.Password.ConfirmMismatch"], status.Text);

            SetPassword(confirm, "senhaseguraaqui");
            Assert.Equal(LocalizationService.Current["Account.Password.ConfirmMatch"], status.Text);
        });
    }

    [Fact]
    public void EyeToggle_RevealsAndHidesWithoutLosingTheValue()
    {
        RunOnUiThread(window =>
        {
            var field = Get<FiveMCleaner.App.Controls.RevealPasswordBox>(window, "PasswordField");
            var toggle = Get<System.Windows.Controls.Primitives.ToggleButton>(field, "RevealToggle");
            var masked = Get<PasswordBox>(field, "Masked");
            var revealed = Get<TextBox>(field, "Revealed");

            SetPassword(field, "minhasenha123");
            Assert.Equal(Visibility.Visible, masked.Visibility);
            Assert.Equal(Visibility.Collapsed, revealed.Visibility);

            toggle.IsChecked = true;
            Assert.Equal(Visibility.Collapsed, masked.Visibility);
            Assert.Equal("minhasenha123", revealed.Text);
            Assert.Equal("minhasenha123", field.Password);

            toggle.IsChecked = false;
            Assert.Equal(Visibility.Visible, masked.Visibility);
            Assert.Equal("minhasenha123", field.Password);
            Assert.Empty(revealed.Text);

            field.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Assert.Empty(masked.Password);
            Assert.Empty(revealed.Text);
            Assert.Empty(field.Password);
        });
    }

    [Fact]
    public void GoogleButton_IsHiddenWhenNoOAuthClientIsConfigured()
    {
        RunOnUiThread(
            window => Assert.Equal(Visibility.Collapsed, Get<UIElement>(window, "ProviderPanel").Visibility),
            googleConfigured: false);
    }

    [Fact]
    public void GoogleButton_IsOfferedWhenConfigured()
    {
        RunOnUiThread(window => Assert.Equal(Visibility.Visible, Get<UIElement>(window, "ProviderPanel").Visibility));
    }

    /// <summary>
    /// Regression guard for the "campo de senha tem uma barrinha embaixo,
    /// os outros ficam flutuando" report: <c>RevealPasswordBox</c>'s
    /// PasswordBox/TextBox must render as a bare
    /// <see cref="System.Windows.Controls.ScrollViewer"/> with nothing else
    /// in their visual tree. Wpf.Ui's own default chrome (the one that drew
    /// the underline) adds more than that; if the implicit style in
    /// RevealPasswordBox.xaml ever stops overriding it, this fails instead
    /// of only being noticeable by eye.
    /// </summary>
    [Fact]
    public void PasswordFields_HaveNoDefaultChromeUnderline()
    {
        RunOnUiThread(window =>
        {
            var field = Get<FiveMCleaner.App.Controls.RevealPasswordBox>(window, "PasswordField");
            var masked = Get<PasswordBox>(field, "Masked");
            var revealed = Get<TextBox>(field, "Revealed");
            masked.ApplyTemplate();
            revealed.ApplyTemplate();

            Assert.Equal(1, System.Windows.Media.VisualTreeHelper.GetChildrenCount(masked));
            Assert.IsType<ScrollViewer>(System.Windows.Media.VisualTreeHelper.GetChild(masked, 0));
            Assert.Equal(1, System.Windows.Media.VisualTreeHelper.GetChildrenCount(revealed));
            Assert.IsType<ScrollViewer>(System.Windows.Media.VisualTreeHelper.GetChild(revealed, 0));
        });
    }

    [Fact]
    public void Window_CannotBeMovedAndOpensWithoutResize()
    {
        RunOnUiThread(window =>
        {
            Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
            Assert.Equal(WindowStartupLocation.CenterOwner, window.WindowStartupLocation);
        });
    }

    [Theory]
    [InlineData(0xF010, true)] // SC_MOVE itself
    [InlineData(0xF012, true)] // SC_MOVE | low-nibble flag some move-initiation paths set
    [InlineData(0xF030, false)] // SC_MAXIMIZE -- a different system command entirely
    [InlineData(0xF060, false)] // SC_CLOSE -- must never be swallowed by the move guard
    public void IsMoveCommand_OnlyMatchesSC_MOVERegardlessOfTheLowNibble(int wParam, bool expected)
    {
        const int WM_SYSCOMMAND = 0x0112;
        const int WM_COMMAND = 0x0111;

        Assert.Equal(expected, AccountWindow.IsMoveCommand(WM_SYSCOMMAND, (IntPtr)wParam));
        // A different message carrying the exact same wParam bits must never match.
        Assert.False(AccountWindow.IsMoveCommand(WM_COMMAND, (IntPtr)wParam));
    }

    /// <summary>
    /// Once the account is fully signed in, the window's job is done: it
    /// must not try to show the (now-removed) management UI, and must close
    /// itself instead. A harness that builds the window without
    /// <see cref="Window.ShowDialog"/> can't set <see cref="Window.DialogResult"/>
    /// (WPF throws), so this also exercises the fallback to
    /// <see cref="Window.Close"/> that keeps that scenario safe.
    /// </summary>
    [Fact]
    public void Window_ClosesInsteadOfShowingManagementUiOnceSignedIn()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationResources();
                var window = new AccountWindow(
                    new SignedInFakeAuthService(),
                    new FakeProfileService(),
                    new FakeGoogleOAuthClient(configured: true));

                // Render() runs off the constructor's Loaded handler and
                // must not throw even though ShowDialog was never called.
                window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "A janela de conta não terminou de ser avaliada a tempo.");
        if (failure is not null) throw failure;
    }

    // ===================== Harness =====================

    private static readonly RoutedEvent ButtonBase_ClickEvent =
        System.Windows.Controls.Primitives.ButtonBase.ClickEvent;

    private static void SetPassword(FiveMCleaner.App.Controls.RevealPasswordBox field, string value)
    {
        if (Get<System.Windows.Controls.Primitives.ToggleButton>(field, "RevealToggle").IsChecked == true)
        {
            Get<TextBox>(field, "Revealed").Text = value;
        }
        else
        {
            Get<PasswordBox>(field, "Masked").Password = value;
        }
    }

    private static string Text(DependencyObject scope, string name) => Get<TextBlock>(scope, name).Text;

    private static T Get<T>(DependencyObject scope, string name) where T : class
    {
        var found = scope is FrameworkElement element ? element.FindName(name) : null;
        Assert.NotNull(found);
        return (T)found!;
    }

    /// <summary>
    /// Calls a private async method and waits for it. The sign-in path is
    /// driven directly instead of through the button so the test does not
    /// depend on the dispatcher pumping an <c>async void</c> handler.
    /// </summary>
    private static void Invoke(AccountWindow window, string method)
    {
        var info = typeof(AccountWindow).GetMethod(
            method,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(info);
        ((Task)info!.Invoke(window, null)!).GetAwaiter().GetResult();
    }

    private static void RunOnUiThread(Action<AccountWindow> assertions, bool googleConfigured = true)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationResources();
                var window = new AccountWindow(
                    new FakeAuthService(),
                    new FakeProfileService(),
                    new FakeGoogleOAuthClient(googleConfigured));

                // Loaded never fires for a window that is never shown, so the
                // initial Render is triggered the same way the handler does.
                window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                assertions(window);
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "A janela de conta não terminou de ser avaliada a tempo.");
        if (failure is not null) throw failure;
    }

    /// <summary>
    /// Mirrors App.xaml: the window resolves brushes, icons and control
    /// styles through Application.Resources, so the same dictionaries have to
    /// be merged here or every StaticResource lookup fails.
    /// </summary>
    private static void EnsureApplicationResources()
    {
        var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (application.Resources.MergedDictionaries.Count > 0) return;

        // App.xaml declares this directly in its own top-level resources
        // (not in a merged dictionary file), so a bare Application built by
        // this test never gets it for free — every StaticResource lookup for
        // localized strings would fail otherwise.
        application.Resources["LocalizedStrings"] = new LocalizedStrings();

        application.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark });
        application.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
        foreach (var theme in new[] { "Tokens/Colors.Dark", "Tokens/Radii", "Tokens/Motion", "Typography", "Surfaces", "Icons", "Controls" })
        {
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/FiveMCleaner;component/Themes/{theme}.xaml", UriKind.Absolute),
            });
        }
    }

    private sealed class FakeAuthService : IFirebaseAuthService
    {
        public AuthenticationSnapshot Current { get; private set; } = new(AuthenticationState.SignedOut, null);
        public event EventHandler<AuthenticationSnapshot>? StateChanged;

        public Task<FirebaseAuthResult> RestoreSessionAsync(CancellationToken cancellationToken = default) => Ok();
        public Task<FirebaseAuthResult> RegisterAsync(string email, string password, bool keepSignedIn, CancellationToken cancellationToken = default) => Ok();

        /// <summary>Always rejects, which is what the "esqueci minha senha" tests need.</summary>
        public Task<FirebaseAuthResult> SignInAsync(string email, string password, bool keepSignedIn, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FirebaseAuthResult(AuthenticationState.SignedOut, null, "Se os dados estiverem corretos, você receberá as instruções necessárias."));

        public Task<FederatedSignInResult> SignInWithGoogleAsync(string googleIdToken, bool keepSignedIn, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FederatedSignInResult(new FirebaseAuthResult(AuthenticationState.SignedOut, null, "indisponível")));

        public Task<FirebaseAuthResult> RefreshEmailVerificationAsync(CancellationToken cancellationToken = default) => Ok();
        public Task<FirebaseAuthResult> RefreshAccountReadinessAsync(CancellationToken cancellationToken = default) => Ok();
        public Task<FirebaseAuthResult> ResendVerificationEmailAsync(CancellationToken cancellationToken = default) => Ok();
        public Task<FirebaseAuthResult> SendPasswordResetEmailAsync(string email, CancellationToken cancellationToken = default) => Ok();
        public Task<FirebaseAuthResult> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default) => Ok();
        public Task<FirebaseAuthResult> ChangeEmailAsync(string currentPassword, string newEmail, CancellationToken cancellationToken = default) => Ok();
        public Task<FirebaseAuthResult> DeleteAccountAsync(string currentPassword, CancellationToken cancellationToken = default) => Ok();
        public Task<string?> GetIdTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }

        private Task<FirebaseAuthResult> Ok()
        {
            StateChanged?.Invoke(this, Current);
            return Task.FromResult(new FirebaseAuthResult(Current.State, Current.User));
        }
    }

    /// <summary>Reports SignedIn from the very first read, so the constructor's Loaded-triggered Render() hits the auto-close branch.</summary>
    private sealed class SignedInFakeAuthService : IFirebaseAuthService
    {
        public AuthenticationSnapshot Current { get; } = new(AuthenticationState.SignedIn, new FirebaseUser("uid-1", "person@example.com", true));
        public event EventHandler<AuthenticationSnapshot>? StateChanged { add { } remove { } }

        public Task<FirebaseAuthResult> RestoreSessionAsync(CancellationToken cancellationToken = default) => Result();
        public Task<FirebaseAuthResult> RegisterAsync(string email, string password, bool keepSignedIn, CancellationToken cancellationToken = default) => Result();
        public Task<FirebaseAuthResult> SignInAsync(string email, string password, bool keepSignedIn, CancellationToken cancellationToken = default) => Result();
        public Task<FederatedSignInResult> SignInWithGoogleAsync(string googleIdToken, bool keepSignedIn, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FederatedSignInResult(new FirebaseAuthResult(Current.State, Current.User)));
        public Task<FirebaseAuthResult> RefreshEmailVerificationAsync(CancellationToken cancellationToken = default) => Result();
        public Task<FirebaseAuthResult> RefreshAccountReadinessAsync(CancellationToken cancellationToken = default) => Result();
        public Task<FirebaseAuthResult> ResendVerificationEmailAsync(CancellationToken cancellationToken = default) => Result();
        public Task<FirebaseAuthResult> SendPasswordResetEmailAsync(string email, CancellationToken cancellationToken = default) => Result();
        public Task<FirebaseAuthResult> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default) => Result();
        public Task<FirebaseAuthResult> ChangeEmailAsync(string currentPassword, string newEmail, CancellationToken cancellationToken = default) => Result();
        public Task<FirebaseAuthResult> DeleteAccountAsync(string currentPassword, CancellationToken cancellationToken = default) => Result();
        public Task<string?> GetIdTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }

        private Task<FirebaseAuthResult> Result() => Task.FromResult(new FirebaseAuthResult(Current.State, Current.User));
    }

    private sealed class FakeProfileService : IAccountProfileService
    {
        public Task<AccountProfileResult> CreateAsync(string idToken, AccountProfileSubmission submission, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccountProfileResult(AccountProfileOutcome.Created, null));

        public Task<AccountProfileFetchResult> FetchAsync(string idToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccountProfileFetchResult(AccountProfileFetchOutcome.NotFound));

        public Task<AccountProfileDeletionResult> DeleteAsync(string idToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccountProfileDeletionResult(AccountProfileDeletionOutcome.Deleted));

        public Task<UsernameAvailability> CheckUsernameAsync(string username, CancellationToken cancellationToken = default) =>
            Task.FromResult(UsernameAvailability.Available);
    }

    private sealed class FakeGoogleOAuthClient(bool configured) : IGoogleOAuthClient
    {
        public bool IsConfigured { get; } = configured;

        public Task<GoogleSignInTicket> AuthenticateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(GoogleSignInTicket.Fail("indisponível no teste"));
    }
}
