using System.Globalization;
using Ralven.App.Services;
using Ralven.App.ViewModels;
using Ralven.Windows.Infrastructure;
using Xunit;

namespace Ralven.Tests.App;

public sealed class ApplicationsPageViewModelTests
{
    [Fact]
    public async Task RefreshAsync_PresentsPartialInventoryAndFiltersInstalledApplications()
    {
        var localization = new LocalizationService(CultureInfo.GetCultureInfo("en-US"));
        var snapshot = new WindowsApplicationInventorySnapshot(
            [
                new("Alpha", "1.0", "Vendor A", 512L * 1024 * 1024,
                    WindowsApplicationScope.CurrentUser, WindowsApplicationArchitecture.X64),
                new("Beta", "2.0", "Vendor B", 1536L * 1024 * 1024,
                    WindowsApplicationScope.LocalMachine, WindowsApplicationArchitecture.X64)
            ],
            [
                new("Ralven", "CurrentUser:RegistryRun", WindowsStartupItemSource.RegistryRun,
                    WindowsApplicationScope.CurrentUser)
            ],
            new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero),
            InstalledApplicationsComplete: false,
            StartupItemsComplete: true);
        using var viewModel = new ApplicationsPageViewModel(
            new StubInventoryInspector(_ => Task.FromResult(snapshot)),
            localization);

        await viewModel.RefreshAsync(TestContext.Current.CancellationToken);
        viewModel.SearchText = "Vendor B";
        viewModel.ShowTechnicalDetails = true;

        Assert.Equal(2, viewModel.InstalledApplicationCount);
        Assert.Equal(1, viewModel.StartupItemCount);
        Assert.Equal(
            localization.GetString("Applications.Inventory.Status.Partial"),
            viewModel.InventoryStatusMessage);
        var application = Assert.Single(viewModel.InstalledApplications);
        Assert.Equal("Beta", application.Name);
        Assert.Equal("1.5 GB", application.EstimatedSize);
        Assert.Empty(viewModel.StartupItems);
        Assert.True(viewModel.ShowTechnicalDetails);
        Assert.Equal(
            localization.GetString("Applications.Inventory.NoMatches.Startup"),
            viewModel.StartupEmptyMessage);
    }

    [Fact]
    public async Task RefreshAsync_DistinguishesEmptyAndIncompleteSubinventories()
    {
        var localization = new LocalizationService(CultureInfo.GetCultureInfo("en-US"));
        var snapshot = new WindowsApplicationInventorySnapshot(
            [],
            [],
            DateTimeOffset.UtcNow,
            InstalledApplicationsComplete: true,
            StartupItemsComplete: false);
        using var viewModel = new ApplicationsPageViewModel(
            new StubInventoryInspector(_ => Task.FromResult(snapshot)),
            localization);

        await viewModel.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.True(viewModel.ShowInstalledEmptyState);
        Assert.True(viewModel.ShowStartupEmptyState);
        Assert.Equal(
            localization.GetString("Applications.Inventory.Empty.Installed"),
            viewModel.InstalledEmptyMessage);
        Assert.Equal(
            localization.GetString("Applications.Inventory.Incomplete.Startup"),
            viewModel.StartupEmptyMessage);
    }

    [Fact]
    public async Task RefreshAsync_WhenInspectionFails_ShowsUnavailableState()
    {
        var localization = new LocalizationService(CultureInfo.GetCultureInfo("pt-BR"));
        using var viewModel = new ApplicationsPageViewModel(
            new StubInventoryInspector(_ => Task.FromException<WindowsApplicationInventorySnapshot>(
                new IOException("test failure"))),
            localization);

        await viewModel.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            $"{localization.GetString("Applications.Inventory.Status.Unavailable")} — "
                + localization.Format("Report.ErrorCodeSuffix", "SYS_FILESYSTEM"),
            viewModel.InventoryStatusMessage);
        Assert.Empty(viewModel.InstalledApplications);
        Assert.Empty(viewModel.StartupItems);
        Assert.True(viewModel.ShowInstalledEmptyState);
        Assert.True(viewModel.ShowStartupEmptyState);
        Assert.Equal(
            localization.GetString("Applications.Inventory.Unavailable.Installed"),
            viewModel.InstalledEmptyMessage);
        Assert.Equal(
            localization.GetString("Applications.Inventory.Unavailable.Startup"),
            viewModel.StartupEmptyMessage);
        Assert.True(viewModel.CanRefreshApplications);

        localization.SetLanguage(AppLanguage.English);
        Assert.Equal(
            localization.GetString("Applications.Inventory.Status.Unavailable"),
            viewModel.InventoryStatusMessage);
        Assert.Equal(
            localization.GetString("Applications.Inventory.Unavailable.Installed"),
            viewModel.InstalledEmptyMessage);
    }

    [Fact]
    public async Task RefreshAsync_FailureAfterSuccess_NotifiesUnavailableState()
    {
        var localization = new LocalizationService(CultureInfo.GetCultureInfo("en-US"));
        var attempts = 0;
        var snapshot = new WindowsApplicationInventorySnapshot(
            [],
            [],
            DateTimeOffset.UtcNow,
            InstalledApplicationsComplete: true,
            StartupItemsComplete: true);
        using var viewModel = new ApplicationsPageViewModel(
            new StubInventoryInspector(_ => ++attempts == 1
                ? Task.FromResult(snapshot)
                : Task.FromException<WindowsApplicationInventorySnapshot>(new IOException("refresh failed"))),
            localization);

        await viewModel.RefreshAsync(TestContext.Current.CancellationToken);
        var changedProperties = new HashSet<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                changedProperties.Add(args.PropertyName);
            }
        };

        await viewModel.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Empty(viewModel.InstalledApplications);
        Assert.Equal(
            $"{localization.GetString("Applications.Inventory.Status.Unavailable")} — "
                + localization.Format("Report.ErrorCodeSuffix", "SYS_FILESYSTEM"),
            viewModel.InventoryStatusMessage);
        Assert.Equal(
            localization.GetString("Applications.Inventory.Unavailable.Installed"),
            viewModel.InstalledEmptyMessage);
        Assert.Contains(nameof(ApplicationsPageViewModel.InstalledEmptyMessage), changedProperties);
        Assert.Contains(nameof(ApplicationsPageViewModel.StartupEmptyMessage), changedProperties);

        localization.SetLanguage(AppLanguage.PortugueseBrazil);
        Assert.Equal(
            localization.GetString("Applications.Inventory.Unavailable.Installed"),
            viewModel.InstalledEmptyMessage);
    }

    [Fact]
    public async Task ApplicationUpdates_AreFilteredAndRemovedAfterSuccessfulUpdate()
    {
        var localization = new LocalizationService(CultureInfo.GetCultureInfo("pt-BR"));
        var packages = new StubPackageService(updates: Snapshot(
            [
                new("OBSProject.OBSStudio", "OBS Studio", "31.0", "32.1", "winget"),
                new("VideoLAN.VLC", "VLC media player", "3.0.21", "3.0.22", "winget")
            ]));
        using var viewModel = new ApplicationsPageViewModel(
            EmptyInventoryInspector(),
            packages,
            new InMemoryApplicationUpdateIgnoreStore(),
            localization);

        await viewModel.CheckApplicationUpdatesAsync(TestContext.Current.CancellationToken);
        viewModel.SearchText = "OBS";
        var item = Assert.Single(viewModel.ApplicationUpdates);
        await viewModel.UpdateApplicationAsync(item, TestContext.Current.CancellationToken);

        Assert.Equal(
            (WindowsApplicationPackageOperation.Update, "OBSProject.OBSStudio"),
            Assert.Single(packages.Executed));
        Assert.Equal(1, viewModel.ApplicationUpdateCount);
        Assert.Empty(viewModel.ApplicationUpdates);
        Assert.Equal(
            localization.Format(
                "Applications.Packages.Operation.Update.Succeeded",
                "OBS Studio"),
            viewModel.ApplicationUpdateStatusMessage);
        Assert.True(viewModel.CanRunPackageOperation);
    }

    [Fact]
    public async Task IgnoredUpdates_PersistAndAreExcludedFromBulkUpdate()
    {
        var localization = new LocalizationService(CultureInfo.GetCultureInfo("en-US"));
        var packageService = new StubPackageService(updates: Snapshot(
        [
            new("OBSProject.OBSStudio", "OBS Studio", "31.0", "32.1", "winget"),
            new("VideoLAN.VLC", "VLC media player", "3.0.21", "3.0.22", "winget")
        ]));
        var ignoreStore = new RecordingIgnoreStore();
        using var viewModel = new ApplicationsPageViewModel(
            EmptyInventoryInspector(),
            packageService,
            ignoreStore,
            localization);

        await viewModel.CheckApplicationUpdatesAsync(TestContext.Current.CancellationToken);
        var ignored = viewModel.ApplicationUpdates.Single(item => item.PackageId == "VideoLAN.VLC");
        await viewModel.SetUpdateIgnoredAsync(
            ignored,
            ignored: true,
            TestContext.Current.CancellationToken);
        viewModel.IsAllVisibleUpdatesSelected = true;
        await viewModel.UpdateSelectedApplicationsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["winget|VideoLAN.VLC"], ignoreStore.Values);
        Assert.Equal(
            (WindowsApplicationPackageOperation.Update, "OBSProject.OBSStudio"),
            Assert.Single(packageService.Executed));
        Assert.Equal(0, viewModel.ApplicationUpdateCount);
        Assert.Empty(viewModel.ApplicationUpdates);
        Assert.Equal(
            localization.GetString("Applications.Updates.Empty.AllIgnored"),
            viewModel.ApplicationUpdatesEmptyMessage);

        viewModel.ShowIgnoredUpdates = true;
        Assert.True(Assert.Single(viewModel.ApplicationUpdates).IsIgnored);
    }

    [Fact]
    public async Task DiscoverInstallAndManagedUninstall_UseTheExactCatalogPackage()
    {
        var package = new WindowsApplicationPackage(
            "VideoLAN.VLC", "VLC media player", "3.0.22", null, "winget");
        var packageService = new StubPackageService(search: Snapshot([package]));
        using var viewModel = new ApplicationsPageViewModel(
            EmptyInventoryInspector(),
            packageService,
            new InMemoryApplicationUpdateIgnoreStore(),
            new LocalizationService(CultureInfo.GetCultureInfo("en-US")))
        {
            DiscoverQuery = "VLC"
        };

        await viewModel.SearchPackagesAsync(TestContext.Current.CancellationToken);
        await viewModel.InstallPackageAsync(
            Assert.Single(viewModel.DiscoveredPackages),
            TestContext.Current.CancellationToken);
        await viewModel.UninstallPackageAsync(
            Assert.Single(viewModel.ManagedPackages),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                (WindowsApplicationPackageOperation.Install, "VideoLAN.VLC"),
                (WindowsApplicationPackageOperation.Uninstall, "VideoLAN.VLC")
            ],
            packageService.Executed);
        Assert.Empty(viewModel.ManagedPackages);
    }

    [Fact]
    public async Task JsonIgnoreStore_RoundTripsValidatedPackageKeys()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "RalvenApplicationUpdateIgnoreStoreTests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "ignores.json");
        try
        {
            var store = new JsonApplicationUpdateIgnoreStore(path);

            await store.SaveAsync(
                new HashSet<string>(["winget|VideoLAN.VLC", "msstore|9NBLGGH4NNS1"]),
                TestContext.Current.CancellationToken);
            var restored = await store.LoadAsync(TestContext.Current.CancellationToken);

            Assert.Equal(
                ["msstore|9NBLGGH4NNS1", "winget|VideoLAN.VLC"],
                restored.Order(StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CheckUpdates_WhenIgnoreStoreIsCorrupt_ShowsUpdatesWithAWarning()
    {
        var localization = new LocalizationService(CultureInfo.GetCultureInfo("en-US"));
        using var viewModel = new ApplicationsPageViewModel(
            EmptyInventoryInspector(),
            new StubPackageService(updates: Snapshot(
            [
                new("VideoLAN.VLC", "VLC media player", "3.0.21", "3.0.22", "winget")
            ])),
            new ThrowingIgnoreStore(),
            localization);

        await viewModel.CheckApplicationUpdatesAsync(TestContext.Current.CancellationToken);

        Assert.Single(viewModel.ApplicationUpdates);
        Assert.Equal(
            localization.GetString("Applications.Updates.Status.IgnoreLoadFailed"),
            viewModel.ApplicationUpdateStatusMessage);
    }

    private sealed class StubInventoryInspector(
        Func<CancellationToken, Task<WindowsApplicationInventorySnapshot>> inspect)
        : IWindowsApplicationInventoryInspector
    {
        public Task<WindowsApplicationInventorySnapshot> InspectAsync(
            CancellationToken cancellationToken = default) => inspect(cancellationToken);

        public Task<WindowsApplicationInventorySnapshot> InspectStartupAsync(
            CancellationToken cancellationToken = default) => inspect(cancellationToken);
    }

    private static StubInventoryInspector EmptyInventoryInspector() => new(_ =>
        Task.FromResult(new WindowsApplicationInventorySnapshot(
            [],
            [],
            DateTimeOffset.UtcNow,
            InstalledApplicationsComplete: true,
            StartupItemsComplete: true)));

    private static WindowsApplicationPackageSnapshot Snapshot(
        IReadOnlyList<WindowsApplicationPackage> packages) => new(
        packages,
        DateTimeOffset.UtcNow,
        IsWinGetAvailable: true,
        UnavailableSources: []);

    private sealed class StubPackageService(
        WindowsApplicationPackageSnapshot? search = null,
        WindowsApplicationPackageSnapshot? installed = null,
        WindowsApplicationPackageSnapshot? updates = null)
        : IWindowsApplicationPackageService
    {
        public List<(WindowsApplicationPackageOperation Operation, string PackageId)> Executed { get; } = [];

        public Task<WindowsApplicationPackageSnapshot> SearchAsync(
            string query,
            CancellationToken cancellationToken = default) => Task.FromResult(search ?? Snapshot([]));

        public Task<WindowsApplicationPackageSnapshot> ListInstalledAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(installed ?? Snapshot([]));

        public Task<WindowsApplicationPackageSnapshot> CheckUpdatesAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(updates ?? Snapshot([]));

        public Task<WindowsApplicationPackageResult> ExecuteAsync(
            WindowsApplicationPackageOperation operation,
            WindowsApplicationPackage package,
            CancellationToken cancellationToken = default)
        {
            Executed.Add((operation, package.PackageId));
            return Task.FromResult(new WindowsApplicationPackageResult(
                WindowsApplicationPackageOutcome.Succeeded,
                ExitCode: 0));
        }
    }

    private sealed class RecordingIgnoreStore : IApplicationUpdateIgnoreStore
    {
        public IReadOnlyList<string> Values { get; private set; } = [];

        public Task<IReadOnlySet<string>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        public Task SaveAsync(
            IReadOnlyCollection<string> packageKeys,
            CancellationToken cancellationToken = default)
        {
            Values = packageKeys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingIgnoreStore : IApplicationUpdateIgnoreStore
    {
        public Task<IReadOnlySet<string>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlySet<string>>(new InvalidDataException("corrupt"));

        public Task SaveAsync(
            IReadOnlyCollection<string> packageKeys,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
