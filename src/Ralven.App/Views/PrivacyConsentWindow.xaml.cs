using System.ComponentModel;
using System.Windows;
using Ralven.App.Services;

namespace Ralven.App.Views;

/// <summary>
/// Blocking, first-run (or renewal) privacy consent screen. Shown by
/// <c>MainWindow</c> right after settings finish loading and only when
/// <see cref="PrivacyConsentEvaluator"/> says a decision is still pending —
/// this window itself has no knowledge of <c>AppSettings</c> persistence; it
/// only presents the optional-reports toggle and reports back the choice confirmed by
/// the user with "Continue".
/// </summary>
public partial class PrivacyConsentWindow : Ralven.App.Controls.DialogWindow
{
    private readonly ILocalizationService localization;
    private bool confirmedByUser;

    public PrivacyConsentWindow(
        PrivacyConsentScreenVariant variant,
        bool initialShareOptionalReports,
        ILocalizationService? localization = null)
    {
        this.localization = localization ?? LocalizationService.Current;
        Variant = variant;
        InitializeComponent();
        DataContext = this;
        OptionalReportsCheckBox.IsChecked = initialShareOptionalReports;
        AcceptedOptionalReports = initialShareOptionalReports;
        Closing += PrivacyConsentWindow_Closing;
        Loaded += (_, _) => OptionalReportsCheckBox.Focus();
    }

    public PrivacyConsentScreenVariant Variant { get; }

    /// <summary>
    /// The user's final choice for optional telemetry and sanitized crash
    /// reports, confirmed with "Continue".
    /// </summary>
    public bool AcceptedOptionalReports { get; private set; }

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

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        AcceptedOptionalReports = OptionalReportsCheckBox.IsChecked == true;
        confirmedByUser = true;
        DialogResult = true;
    }

    private void PrivacyConsentWindow_Closing(object? sender, CancelEventArgs e)
    {
        e.Cancel = !confirmedByUser;
    }

    private string T(string key) => localization.GetString(key);
}
