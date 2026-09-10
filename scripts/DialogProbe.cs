using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ralven.App;
using Ralven.App.Controls;
using Ralven.App.Services;
using Ralven.App.Views;
using Button = System.Windows.Controls.Button;
using Size = System.Windows.Size;

// Explicit development entry point; no App startup, real account, network or settings writes.
internal static class DialogProbe
{
    [STAThread]
    private static void Main(string[] args)
    {
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/dialog-captures");
        Directory.CreateDirectory(output);
        var app = new ProbeApplication { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        LocalizationService.Current.SetLanguage(AppLanguage.PortugueseBrazil);
        using var client = new HttpClient(new OfflineHandler());
        var profiles = new DisabledAccountProfileService();
        var auth = new FirebaseAuthService(client, string.Empty,
            new SecureFirebaseSessionStore(Path.Combine(output, "unused.session")), profiles);
        var google = new ProbeGoogle();
        using var theme = new ThemeManager();
        var owner = new Window
        {
            Width = 1100, Height = 800, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new AdornerDecorator { Child = new Button { Content = "Dialog probe" } }
        };
        owner.Show();
        var factories = new (string Name, Func<DialogWindow> Create, Action<DialogWindow>? Prepare)[]
        {
            ("login", () => new AccountWindow(auth, profiles, google), null),
            ("registration", () => new AccountWindow(auth, profiles, google), window => Click(window, "SwitchButton")),
            ("bug", () => new BugReportWindow(new DisabledBugReportService(), "1.6.1", "Leve", "Legacy"), null),
            ("password", () => new PasswordSecurityWindow(auth, google), null),
            ("privacy", () => new PrivacyConsentWindow(PrivacyConsentScreenVariant.FirstInstallation, true), null),
            ("release", () => new ReleaseNotesWindow(ReleaseNotesCatalog.Versions[0]), null),
            ("terms", () => new TermsOfUseWindow(), null),
            ("confirmation", () => new OptimizationConfirmationWindow("Restaurar alterações", "As alterações desta execução serão restauradas. Confira o plano antes de continuar.", "Cancelar", "Restaurar"), null)
        };
        foreach (var preference in new[] { AppThemePreference.Dark, AppThemePreference.Light })
        {
            theme.Apply(preference);
            foreach (var (name, create, prepare) in factories)
            {
                foreach (var small in new[] { false, true })
                {
                    var window = create();
                    window.Owner = owner;
                    prepare?.Invoke(window);
                    if (small)
                    {
                        window.MinWidth = 0; window.MinHeight = 0;
                        window.Width = 480; window.Height = 520;
                    }
                    Exception? failure = null;
                    window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
                    {
                        try
                        {
                            if (small) { window.Width = 480; window.Height = 520; }
                            window.UpdateLayout();
                            Check(!small || window.ActualWidth == 480 && window.ActualHeight == 520, "Compact viewport was not applied");
                            Check(window.IsKeyboardFocusWithin, $"{name}: focus escaped the modal");
                            var content = (FrameworkElement)window.Content;
                            Check(content.ActualWidth > 0 && content.ActualHeight > 0, $"{name}: empty content");
                            var footer = Descendants(content).OfType<Button>().LastOrDefault(button => button.IsVisible);
                            Check(footer is not null, $"{name}: no available action");
                            for (var tab = 0; tab < 24; tab++)
                            {
                                if (Keyboard.FocusedElement is UIElement focused)
                                    focused.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                                Check(window.IsKeyboardFocusWithin, $"{name}: Tab escaped the modal");
                            }
                            if (name == "bug")
                            {
                                var selector = (System.Windows.Controls.ComboBox)window.FindName("BugCodeComboBox");
                                Check(!Descendants(selector).OfType<TextBlock>().Any(text => text.Text.Contains("BugCodeOption", StringComparison.Ordinal)),
                                    "Selector exposed its DTO instead of the localized label");
                                selector.IsDropDownOpen = true;
                                Escape(window, selector);
                                Check(!selector.IsDropDownOpen, "Escape did not close the selector");
                                Check(window.IsVisible, "Escape closed the dialog while the selector was open");
                                selector.IsDropDownOpen = false;
                                var description = (System.Windows.Controls.TextBox)window.FindName("DescriptionTextBox");
                                description.Text = string.Join(Environment.NewLine, Enumerable.Repeat("Synthetic multiline scroll validation", 80));
                                description.UpdateLayout();
                                description.ScrollToEnd();
                                description.UpdateLayout();
                                Check(description.VerticalOffset > 0, "Multiline field cannot scroll");
                                description.Clear();
                                description.UpdateLayout();
                            }
                            if (name == "registration" && !small)
                            {
                                var terms = new TermsOfUseWindow { Owner = window };
                                terms.Loaded += (_, _) => terms.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => Escape(terms));
                                _ = terms.ShowDialog();
                                Check(window.IsVisible, "Closing terms dismissed the registration form");
                            }
                            // Capture the real templates with animation completed, not an HTML approximation.
                            if (window.Template.FindName("PART_Surface", window) is UIElement surface)
                                surface.BeginAnimation(UIElement.OpacityProperty, null);
                            foreach (var scroll in Descendants(content).OfType<ScrollViewer>()) scroll.ScrollToHome();
                            if (name == "bug")
                            {
                                var option = Descendants(content).OfType<System.Windows.Controls.RadioButton>().First();
                                option.IsChecked = true;
                                option.Focus();
                            }
                            window.UpdateLayout();
                            Save(window, Path.Combine(output, $"{preference}-{name}-{(small ? "480x520" : "normal")}.png"));
                            foreach (var scroll in Descendants(content).OfType<ScrollViewer>())
                            {
                                scroll.ScrollToEnd();
                                scroll.UpdateLayout();
                                Check(scroll.HorizontalOffset == 0 || scroll.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled,
                                    $"{name}: unexpected horizontal scroll");
                            }
                            if (name == "privacy")
                            {
                                window.Close();
                                Check(window.IsVisible, "Consent could close without an explicit decision");
                                Click(window, "ContinueButton");
                            }
                            else if (name == "confirmation")
                            {
                                Check(!((Button)window.FindName("ConfirmAction")).IsDefault, "Unsafe default confirmation");
                                Click(window, "DismissButton");
                            }
                            else Escape(window);
                        }
                        catch (Exception error) { failure = error; window.CanDismiss = true; window.Close(); }
                    });
                    var result = window.ShowDialog();
                    if (failure is not null) throw failure;
                    Check(name != "privacy" || result == true, "Consent decision was lost");
                    Console.WriteLine($"PASS {preference} {name} {(small ? "480x520" : "normal")}");
                }
            }
        }
        owner.Close();
        app.Shutdown();
    }

    private static void Click(Window window, string name) =>
        ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static void Escape(Window window, UIElement? target = null)
    {
        target ??= window;
        var key = new System.Windows.Input.KeyEventArgs(
            Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(key);
        if (!key.Handled && window.IsVisible)
        {
            key.RoutedEvent = Keyboard.KeyDownEvent;
            target.RaiseEvent(key);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Save(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private sealed class ProbeApplication : App
    {
        protected override void OnStartup(StartupEventArgs e) { }
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The dialog probe must never send network requests.");
    }

    private sealed class ProbeGoogle : IGoogleOAuthClient
    {
        public bool IsConfigured => true;
        public Task<GoogleSignInTicket> AuthenticateAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The dialog probe must never authenticate.");
    }
}
