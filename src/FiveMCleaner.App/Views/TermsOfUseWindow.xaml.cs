using FiveMCleaner.App.Services;

namespace FiveMCleaner.App.Views;

public partial class TermsOfUseWindow : Wpf.Ui.Controls.FluentWindow
{
    public TermsOfUseWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    public string VersionLabel => $"Versão {AccountTerms.CurrentVersion} · Vigente desde 2 de agosto de 2026";
}
