using System.Windows;
using System.Windows.Controls;
using Ralven.Contracts;
using UserControl = System.Windows.Controls.UserControl;

namespace Ralven.App.Views.Pages;

public partial class ProPage : UserControl
{
    public ProPage() => InitializeComponent();
    private MainWindow? Shell => Window.GetWindow(this) as MainWindow;
    private void Free_Click(object sender, RoutedEventArgs e) => Shell?.RequestNavigateToOptimizer(OptimizationScope.GeneralWindows);
    private void SignIn_Click(object sender, RoutedEventArgs e) => Shell?.RequestProSignIn();
    private async void Refresh_Click(object sender, RoutedEventArgs e) { if (Shell is { } shell) await shell.RequestRefreshBillingAsync(); }
    private async void Checkout_Click(object sender, RoutedEventArgs e) { if (Shell is { } shell) await shell.RequestCheckoutAsync(); }
    private async void Cancel_Click(object sender, RoutedEventArgs e) { if (Shell is { } shell) await shell.RequestCancelSubscriptionAsync(); }
    private void Support_Click(object sender, RoutedEventArgs e) => Shell?.RequestBillingSupport();

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 900;
        OfferColumn.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(SubscriptionPanel, compact ? 0 : 1);
        Grid.SetRow(SubscriptionPanel, compact ? 1 : 0);
        BenefitsPanel.Margin = compact ? new Thickness(0, 0, 0, 16) : new Thickness(0, 0, 20, 0);
    }
}
