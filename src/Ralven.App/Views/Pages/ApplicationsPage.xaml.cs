using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Button = System.Windows.Controls.Button;
using Ralven.App.Services;
using Ralven.App.ViewModels;
using Ralven.Windows.Infrastructure;
using UserControl = System.Windows.Controls.UserControl;

namespace Ralven.App.Views.Pages;

public partial class ApplicationsPage : UserControl, IDisposable
{
    private readonly ApplicationsPageViewModel viewModel;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private bool initialRefreshStarted;
    private bool disposed;

    public ApplicationsPage()
        : this(
            new WindowsApplicationInventoryInspector(),
            new WinGetApplicationPackageService(),
            new JsonApplicationUpdateIgnoreStore())
    {
    }

    internal ApplicationsPage(
        IWindowsApplicationInventoryInspector inspector,
        IWindowsApplicationPackageService packageService,
        IApplicationUpdateIgnoreStore ignoreStore)
    {
        InitializeComponent();
        viewModel = new ApplicationsPageViewModel(inspector, packageService, ignoreStore);
        DataContext = viewModel;

        // Setting IsChecked in XAML fires Checked while named siblings are
        // still being created. Select both independent surfaces after the
        // document has been initialized instead.
        UpdatesTab.IsChecked = true;
        InstalledTab.IsChecked = true;
        ShowPackagePanel(ApplicationUpdatesPanel);
        ShowInventoryPanel(InstalledApplicationsPanel);
    }

    private async void ApplicationsPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (initialRefreshStarted)
        {
            return;
        }

        initialRefreshStarted = true;
        await RefreshAllAsync();
    }

    private async void RefreshInventory_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAllAsync();
    }

    private void PackageTab_Checked(object sender, RoutedEventArgs e)
    {
        if (DiscoverPackagesPanel is null
            || ManagedPackagesPanel is null
            || ApplicationUpdatesPanel is null)
        {
            return;
        }

        ShowPackagePanel(sender switch
        {
            _ when ReferenceEquals(sender, DiscoverTab) => DiscoverPackagesPanel,
            _ when ReferenceEquals(sender, ManagedTab) => ManagedPackagesPanel,
            _ => ApplicationUpdatesPanel
        });
    }

    private void ShowPackagePanel(UIElement selected)
    {
        ApplicationUpdatesPanel.Visibility = ReferenceEquals(
            selected,
            ApplicationUpdatesPanel)
            ? Visibility.Visible
            : Visibility.Collapsed;
        DiscoverPackagesPanel.Visibility = ReferenceEquals(selected, DiscoverPackagesPanel)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ManagedPackagesPanel.Visibility = ReferenceEquals(selected, ManagedPackagesPanel)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void InventoryTab_Checked(object sender, RoutedEventArgs e)
    {
        if (InstalledApplicationsPanel is null || StartupApplicationsPanel is null)
        {
            return;
        }

        ShowInventoryPanel(ReferenceEquals(sender, StartupTab)
            ? StartupApplicationsPanel
            : InstalledApplicationsPanel);
    }

    private void ShowInventoryPanel(UIElement selected)
    {
        InstalledApplicationsPanel.Visibility = ReferenceEquals(
            selected,
            InstalledApplicationsPanel)
            ? Visibility.Visible
            : Visibility.Collapsed;
        StartupApplicationsPanel.Visibility = ReferenceEquals(
            selected,
            StartupApplicationsPanel)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async Task RefreshAllAsync()
    {
        await Task.WhenAll(
            viewModel.RefreshAsync(lifetimeCancellation.Token),
            viewModel.RefreshManagedPackagesAsync(lifetimeCancellation.Token),
            viewModel.CheckApplicationUpdatesAsync(lifetimeCancellation.Token));
    }

    private async void SearchPackages_Click(object sender, RoutedEventArgs e) =>
        await viewModel.SearchPackagesAsync(lifetimeCancellation.Token);

    private async void DiscoverQuery_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await viewModel.SearchPackagesAsync(lifetimeCancellation.Token);
    }

    private async void InstallPackage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ApplicationPackageDisplayItem item }
            || !ConfirmPackageOperation(
                "Applications.Discover.Confirm.Title",
                "Applications.Discover.Confirm.Message",
                "Applications.Discover.Confirm.Cancel",
                "Applications.Discover.Confirm.Action",
                item.Name,
                item.PackageId,
                item.Source))
        {
            return;
        }

        await viewModel.InstallPackageAsync(item, lifetimeCancellation.Token);
    }

    private async void UninstallPackage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ApplicationPackageDisplayItem item }
            || !ConfirmPackageOperation(
                "Applications.Packages.Uninstall.Confirm.Title",
                "Applications.Packages.Uninstall.Confirm.Message",
                "Applications.Packages.Uninstall.Confirm.Cancel",
                "Applications.Packages.Uninstall.Confirm.Action",
                item.Name,
                item.PackageId,
                item.Source))
        {
            return;
        }

        await viewModel.UninstallPackageAsync(item, lifetimeCancellation.Token);
    }

    private async void UpdateApplication_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button
            {
                Tag: ApplicationUpdateDisplayItem item
            })
        {
            return;
        }

        var localization = LocalizationService.Current;
        var dialog = new OptimizationConfirmationWindow(
            localization.GetString("Applications.Updates.Confirm.Title"),
            localization.Format(
                "Applications.Updates.Confirm.Message",
                item.Name,
                item.InstalledVersion,
                item.AvailableVersion,
                item.Source),
            localization.GetString("Applications.Updates.Confirm.Cancel"),
            localization.GetString("Applications.Updates.Confirm.Action"))
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await viewModel.UpdateApplicationAsync(item, lifetimeCancellation.Token);
    }

    private async void UpdateSelectedApplications_Click(object sender, RoutedEventArgs e)
    {
        var selected = viewModel.GetSelectedUpdates();
        if (selected.Count == 0)
        {
            return;
        }

        var localization = LocalizationService.Current;
        var dialog = new OptimizationConfirmationWindow(
            localization.GetString("Applications.Updates.BatchConfirm.Title"),
            localization.Format("Applications.Updates.BatchConfirm.Message", selected.Count),
            localization.GetString("Applications.Updates.BatchConfirm.Cancel"),
            localization.GetString("Applications.Updates.BatchConfirm.Action"))
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() == true)
        {
            await viewModel.UpdateSelectedApplicationsAsync(lifetimeCancellation.Token);
        }
    }

    private async void ToggleIgnoredUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ApplicationUpdateDisplayItem item })
        {
            await viewModel.SetUpdateIgnoredAsync(
                item,
                ignored: !item.IsIgnored,
                lifetimeCancellation.Token);
        }
    }

    private bool ConfirmPackageOperation(
        string titleKey,
        string messageKey,
        string cancelKey,
        string actionKey,
        params object[] arguments)
    {
        var localization = LocalizationService.Current;
        var dialog = new OptimizationConfirmationWindow(
            localization.GetString(titleKey),
            localization.Format(messageKey, arguments),
            localization.GetString(cancelKey),
            localization.GetString(actionKey))
        {
            Owner = Window.GetWindow(this)
        };
        return dialog.ShowDialog() == true;
    }

    private void OpenInstalledApps_Click(object sender, RoutedEventArgs e) =>
        OpenSettings("ms-settings:appsfeatures");

    private void OpenStartupApps_Click(object sender, RoutedEventArgs e) =>
        OpenSettings("ms-settings:startupapps");

    private void OpenStoreUpdates_Click(object sender, RoutedEventArgs e) =>
        OpenSettings("ms-windows-store://downloadsandupdates");

    private void OpenDefaultApps_Click(object sender, RoutedEventArgs e) =>
        OpenSettings("ms-settings:defaultapps");

    private static void OpenSettings(string uri) => ExternalLauncher.TryOpen(
        () => Process.Start(new ProcessStartInfo(uri)
        {
            UseShellExecute = true
        }));

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifetimeCancellation.Cancel();
        lifetimeCancellation.Dispose();
        viewModel.Dispose();
    }
}
