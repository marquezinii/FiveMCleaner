using Ralven.App.Services;

namespace Ralven.App.Views;

public partial class TermsOfUseWindow : Ralven.App.Controls.DialogWindow
{
    public TermsOfUseWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    public string VersionLabel => LocalizationService.Current.Format("Terms.VersionLabel", AccountTerms.CurrentVersion);
}
