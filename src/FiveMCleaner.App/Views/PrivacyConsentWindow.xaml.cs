using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using FiveMCleaner.App.Services;

namespace FiveMCleaner.App.Views;

/// <summary>
/// Blocking, first-run (or renewal) privacy consent screen. Shown by
/// <c>MainWindow</c> right after settings finish loading and only when
/// <see cref="PrivacyConsentEvaluator"/> says a decision is still pending —
/// this window itself has no knowledge of <c>AppSettings</c> persistence; it
/// only presents the two toggles and reports back what the user chose (or
/// that they closed the window, which the caller treats as declining both).
/// </summary>
public partial class PrivacyConsentWindow : Window
{
    private readonly ILocalizationService localization;
    private bool confirmedByUser;

    public PrivacyConsentWindow(
        PrivacyConsentScreenVariant variant,
        bool initialShareAnonymousTelemetry,
        bool initialShareCrashReports,
        ILocalizationService? localization = null)
    {
        this.localization = localization ?? LocalizationService.Current;
        Variant = variant;
        InitializeComponent();
        DataContext = this;
        TelemetryCheckBox.IsChecked = initialShareAnonymousTelemetry;
        CrashReportsCheckBox.IsChecked = initialShareCrashReports;
        AcceptedAnonymousTelemetry = initialShareAnonymousTelemetry;
        AcceptedCrashReports = initialShareCrashReports;
        Closing += PrivacyConsentWindow_Closing;
        Loaded += (_, _) => TelemetryCheckBox.Focus();
    }

    public PrivacyConsentScreenVariant Variant { get; }

    /// <summary>
    /// The user's final choice for anonymous telemetry: either what they set
    /// on "Continue", or <see langword="false"/> if they closed the window
    /// instead.
    /// </summary>
    public bool AcceptedAnonymousTelemetry { get; private set; }

    /// <summary>Same rule as <see cref="AcceptedAnonymousTelemetry"/>, for crash reports.</summary>
    public bool AcceptedCrashReports { get; private set; }

    public string HeadingText => Variant switch
    {
        PrivacyConsentScreenVariant.UpgradeFromOlderInstallation => T("PrivacyConsent.Heading.Upgrade"),
        PrivacyConsentScreenVariant.ConsentRenewalRequired => T("PrivacyConsent.Heading.Renewal"),
        _ => T("PrivacyConsent.Heading.FirstInstallation")
    };

    public string IntroText => Variant switch
    {
        PrivacyConsentScreenVariant.UpgradeFromOlderInstallation => T("PrivacyConsent.Intro.Upgrade"),
        PrivacyConsentScreenVariant.ConsentRenewalRequired => T("PrivacyConsent.Intro.Renewal"),
        _ => T("PrivacyConsent.Intro.FirstInstallation")
    };

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        AcceptedAnonymousTelemetry = TelemetryCheckBox.IsChecked == true;
        AcceptedCrashReports = CrashReportsCheckBox.IsChecked == true;
        confirmedByUser = true;
        DialogResult = true;
    }

    private void PrivacyConsentWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!confirmedByUser)
        {
            AcceptedAnonymousTelemetry = false;
            AcceptedCrashReports = false;
        }
    }

    private string T(string key) => localization.GetString(key);
}
