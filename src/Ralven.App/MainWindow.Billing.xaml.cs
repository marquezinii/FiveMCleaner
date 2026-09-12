using System.Diagnostics;
using System.Windows;
using Ralven.App.Services;
using Ralven.App.ViewModels;
using Ralven.App.Views;
using Ralven.App.Views.Pages;
using Ralven.Contracts;

namespace Ralven.App;

public partial class MainWindow
{
    private readonly ProPageViewModel proViewModel = new();
    private readonly CancellationTokenSource billingLifetime = new();
    private CloudflareBillingService? billingService;
    private ProPage? proPage;
    private string? billingUid;
    private int billingSessionVersion;
    private bool refreshBillingOnActivation;

    private ProPage ProPage
    {
        get
        {
            if (proPage is not null) return proPage;
            proPage = new ProPage { DataContext = proViewModel, Visibility = Visibility.Collapsed };
            PageContentHost.Children.Add(proPage);
            return proPage;
        }
    }

    internal void RequestNavigateToPro()
    {
        UpdateBillingSession();
        ActivateNavItem(ProNav);
        Navigate(ProPage);
    }

    private void OpenPro_Click(object sender, RoutedEventArgs e) => RequestNavigateToPro();
    internal void RequestProSignIn()
    {
        if (accountService is null) { proViewModel.ShowMessage("Pro.Unavailable.Description"); return; }
        OpenAccountWindow();
        UpdateBillingSession();
        _ = RequestRefreshBillingAsync();
    }
    internal void RequestBillingSupport() => TryOpenExternal(() => Process.Start(new ProcessStartInfo
    {
        FileName = ProductIdentity.DiscordInviteUrl,
        UseShellExecute = true,
    }));

    private void UpdateBillingSession()
    {
        var uid = accountService?.Current is { State: AuthenticationState.SignedIn, User: { } user } ? user.Uid : null;
        if (billingUid == uid && proViewModel.IsDemo == demoMode) return;
        billingUid = uid;
        billingSessionVersion++;
        refreshBillingOnActivation = false;
        proViewModel.SetSession(uid is not null, demoMode);
    }

    internal async Task RequestRefreshBillingAsync()
    {
        UpdateBillingSession();
        if (!proViewModel.CanRefresh) return;
        await RunBillingOperationAsync(async token =>
        {
            var result = await billingService!.FetchAsync(token, billingLifetime.Token);
            return (result.Value, (Uri?)null, result.Error);
        });
    }

    internal async Task RequestCheckoutAsync()
    {
        if (!proViewModel.CanCheckout || proViewModel.Offer is not { } offer) return;
        await RunBillingOperationAsync(async token =>
        {
            var result = await billingService!.CheckoutAsync(token, offer.Key, billingLifetime.Token);
            return ((BillingSnapshot?)null, result.Value, result.Error);
        }, checkout: true);
    }

    internal async Task RequestCancelSubscriptionAsync()
    {
        if (!proViewModel.CanCancel) return;
        var local = LocalizationService.Current;
        var dialog = new OptimizationConfirmationWindow(
            local.GetString("Pro.Cancel.Title"), local.GetString("Pro.Cancel.Message"),
            local.GetString("Pro.Cancel.Keep"), local.GetString("Pro.Cancel.Confirm"))
        { Owner = this };
        if (dialog.ShowDialog() != true || !proViewModel.CanCancel) return;
        await RunBillingOperationAsync(async token =>
        {
            var result = await billingService!.CancelAsync(token, billingLifetime.Token);
            return (result.Value, (Uri?)null, result.Error);
        }, cancelling: true);
    }

    private async Task RunBillingOperationAsync(
        Func<string, Task<(BillingSnapshot? Snapshot, Uri? Checkout, string? Error)>> operation,
        bool checkout = false, bool cancelling = false)
    {
        if (proViewModel.IsBusy || demoMode || billingUid is not { } uid || accountService is null || billingService is null)
        {
            if (!demoMode) proViewModel.ShowMessage("Pro.Unavailable.Description");
            return;
        }
        var version = billingSessionVersion;
        bool IsCurrent() => !billingLifetime.IsCancellationRequested && version == billingSessionVersion
            && accountService.Current is { State: AuthenticationState.SignedIn, User: { } user } && user.Uid == uid;
        proViewModel.SetBusy(true);
        proViewModel.ShowMessage(null);
        try
        {
            var token = await accountService.GetIdTokenAsync();
            if (!IsCurrent()) return;
            if (token is null) { proViewModel.ShowMessage("Pro.Error.Request"); return; }
            var result = await operation(token);
            if (!IsCurrent()) return;
            if (result.Error is not null)
            {
                if (!checkout && !cancelling) proViewModel.SetSnapshot(null);
                proViewModel.ShowMessage(ProPageViewModel.ErrorKey(result.Error));
                return;
            }
            if (result.Snapshot is not null) proViewModel.SetSnapshot(result.Snapshot);
            if (checkout && result.Checkout is { } checkoutUri)
            {
                // The service validates the provider URL. Never attach the Firebase token to the browser URL.
                Process.Start(new ProcessStartInfo { FileName = checkoutUri.AbsoluteUri, UseShellExecute = true });
                refreshBillingOnActivation = true;
                proViewModel.Consent = false;
                proViewModel.ShowMessage("Pro.Checkout.Opened");
            }
            if (cancelling) proViewModel.ShowMessage("Pro.Cancel.Success");
            if (!checkout) await SyncAccountEntitlementAsync(uid);
        }
        catch (OperationCanceledException) when (billingLifetime.IsCancellationRequested) { }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            if (IsCurrent()) proViewModel.ShowMessage("Pro.Error.Request");
        }
        finally
        {
            proViewModel.SetBusy(false);
        }
    }

    private async void BillingWindow_Activated(object? sender, EventArgs e)
    {
        if (!refreshBillingOnActivation || proViewModel.IsBusy) return;
        refreshBillingOnActivation = false;
        await RequestRefreshBillingAsync();
    }
}
