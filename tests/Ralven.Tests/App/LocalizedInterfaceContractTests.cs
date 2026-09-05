using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Ralven.App;
using Ralven.App.Services;
using Ralven.Contracts;
using Ralven.Core.Catalog;
using Xunit;

namespace Ralven.Tests.App;

public sealed partial class LocalizedInterfaceContractTests
{
    [Fact]
    public void LocalizedRunBindings_AreOneWay()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var appDirectory = Path.Combine(root, "src", "Ralven.App");
        var violations = Directory
            .EnumerateFiles(appDirectory, "*.xaml", SearchOption.AllDirectories)
            .SelectMany(path => LocalizedRunWithoutOneWayPattern().Matches(File.ReadAllText(path)))
            .Select(match => match.Value)
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void LocalizedXamlBindings_ResolveInEnglishAndPortuguese()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var sources = new[]
        {
            Path.Combine(root, "src", "Ralven.App", "MainWindow.xaml"),
            Path.Combine(root, "src", "Ralven.App", "Controls", "UltraPanel.xaml"),
            Path.Combine(root, "src", "Ralven.App", "Views", "BugReportWindow.xaml"),
            Path.Combine(root, "src", "Ralven.App", "Views", "PrivacyConsentWindow.xaml"),
            Path.Combine(root, "src", "Ralven.App", "Views", "ReleaseNotesWindow.xaml"),
            Path.Combine(root, "src", "Ralven.App", "Views", "PasswordSecurityWindow.xaml"),
            Path.Combine(root, "src", "Ralven.App", "Views", "TermsOfUseWindow.xaml"),
            Path.Combine(root, "src", "Ralven.App", "Views", "OptimizationConfirmationWindow.xaml"),
            Path.Combine(root, "src", "Ralven.App", "Views", "Pages", "OverviewPage.xaml"),
            Path.Combine(root, "src", "Ralven.App", "Views", "Pages", "SystemPage.xaml"),
            Path.Combine(root, "src", "Ralven.App", "Views", "Pages", "ApplicationsPage.xaml"),
            Path.Combine(root, "src", "Ralven.App", "Views", "Pages", "GamesPage.xaml"),
            Path.Combine(root, "src", "Ralven.App", "Views", "Pages", "OptimizerPage.xaml")
        };
        var keys = sources
            .SelectMany(path => LocalizedKeyPattern().Matches(File.ReadAllText(path)))
            .Select(match => match.Groups["key"].Value)
            .ToSortedSet(StringComparer.Ordinal);
        var english = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        var portuguese = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        var spanish = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo("es"));

        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            Assert.NotEqual(key, english.GetString(key));
            Assert.NotEqual(key, portuguese.GetString(key));
            Assert.NotEqual(key, spanish.GetString(key));
        }
    }

    [Fact]
    public void GeneralExpansion_UsesInternalCatalogsAndTrustedWindowsActions()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var appDirectory = Path.Combine(root, "src", "Ralven.App");
        var mainWindow = File.ReadAllText(Path.Combine(appDirectory, "MainWindow.xaml"));
        var navigation = File.ReadAllText(Path.Combine(appDirectory, "MainWindow.Navigation.xaml.cs"));
        var capture = File.ReadAllText(Path.Combine(appDirectory, "MainWindow.Capture.xaml.cs"));
        var gamesPage = File.ReadAllText(Path.Combine(appDirectory, "Views", "Pages", "GamesPage.xaml"));
        var systemPage = File.ReadAllText(Path.Combine(appDirectory, "Views", "Pages", "SystemPage.xaml.cs"));
        var systemMarkup = File.ReadAllText(Path.Combine(appDirectory, "Views", "Pages", "SystemPage.xaml"));
        var applicationsView = File.ReadAllText(Path.Combine(appDirectory, "Views", "Pages", "ApplicationsPage.xaml"));
        var applicationsPage = File.ReadAllText(Path.Combine(appDirectory, "Views", "Pages", "ApplicationsPage.xaml.cs"));
        var inspector = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Ralven.Windows",
            "Infrastructure",
            "WindowsApplicationInventoryInspector.cs"));

        Assert.Contains("Tag=\"System\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Tag=\"Applications\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Tag=\"Games\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Tag=\"Optimizer\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("[Navigation.Games]", mainWindow, StringComparison.Ordinal);
        Assert.Contains("\"Games\" => GamesPage", navigation, StringComparison.Ordinal);
        Assert.Contains("OptimizationScope.FiveMLegacy ? GamesNav : OptimizerNav", navigation, StringComparison.Ordinal);
        Assert.Contains("Games.FiveM.Action", gamesPage, StringComparison.Ordinal);
        Assert.Contains("FiveMGameCardSurface", gamesPage, StringComparison.Ordinal);
        Assert.Contains("Assets/FiveM.png", gamesPage, StringComparison.Ordinal);
        Assert.Contains("Height=\"570\"", gamesPage, StringComparison.Ordinal);
        Assert.Contains("\"Games\" => (Element: (UIElement)GamesPage, Nav: GamesNav)", capture, StringComparison.Ordinal);
        Assert.Contains("\"Optimizer\" => ConfigureOptimizerCapture(OptimizationScope.GeneralWindows, OptimizerNav)", capture, StringComparison.Ordinal);
        Assert.Contains("\"FiveMOptimizer\" => ConfigureOptimizerCapture(OptimizationScope.FiveMLegacy, GamesNav)", capture, StringComparison.Ordinal);
        Assert.Contains("ms-settings:windowsupdate", systemPage, StringComparison.Ordinal);
        Assert.Contains("ApplyWindowsGamingSettingsAsync", systemPage, StringComparison.Ordinal);
        Assert.Contains("RestoreWindowsGamingSettingsAsync", systemPage, StringComparison.Ordinal);
        Assert.Contains("WindowsAntivirusHealthLabel", systemMarkup, StringComparison.Ordinal);
        Assert.Contains("WindowsFirewallHealthLabel", systemMarkup, StringComparison.Ordinal);
        Assert.Contains("WindowsAutomaticUpdatesHealthLabel", systemMarkup, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CpuName}\"", systemMarkup, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding GpuDetail}\"", systemMarkup, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding RamLabel}\"", systemMarkup, StringComparison.Ordinal);
        Assert.Contains("RefreshWindowsSystemHealthAsync", systemPage, StringComparison.Ordinal);
        Assert.Contains("RefreshDiagnosticAsync", systemPage, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding InstalledApplications}\"", applicationsView, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding StartupItems}\"", applicationsView, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding SearchText", applicationsView, StringComparison.Ordinal);
        Assert.Contains("ms-settings:appsfeatures", applicationsPage, StringComparison.Ordinal);
        Assert.Contains("ms-windows-store://downloadsandupdates", applicationsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("UninstallString", inspector, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StartupApproved", inspector, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Arguments =", systemPage + applicationsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("runas", systemPage + applicationsPage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WindowsGamingControls_UseNativeButtonsAndCoordinateWithOptimizerBusyState()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var appDirectory = Path.Combine(root, "src", "Ralven.App");
        var page = File.ReadAllText(Path.Combine(appDirectory, "Views", "Pages", "SystemPage.xaml"));
        var controls = File.ReadAllText(Path.Combine(appDirectory, "Themes", "Controls.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(appDirectory, "ViewModels", "MainViewModel.System.cs"));
        var mainViewModel = File.ReadAllText(Path.Combine(appDirectory, "ViewModels", "MainViewModel.cs"));
        var historyViewModel = File.ReadAllText(Path.Combine(
            appDirectory,
            "ViewModels",
            "MainViewModel.Diagnostics.cs"));
        var historyPage = File.ReadAllText(Path.Combine(
            appDirectory,
            "Views",
            "Pages",
            "HistoryPage.xaml.cs"));

        Assert.Contains("System.Gaming.Title", page, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", page, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding CanApplyWindowsGamingSettings}\"", page, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding CanRestoreWindowsGamingSettings}\"", page, StringComparison.Ordinal);
        Assert.Contains(
            "<Setter Property=\"Foreground\" Value=\"{DynamicResource AppTextOnAccentBrush}\" />",
            controls,
            StringComparison.Ordinal);
        Assert.Contains(
            "<Setter Property=\"Foreground\" Value=\"{DynamicResource TextTertiaryBrush}\" />",
            controls,
            StringComparison.Ordinal);
        Assert.Contains("!IsBusy", viewModel, StringComparison.Ordinal);
        Assert.Contains("!isWindowsGamingBusy", mainViewModel, StringComparison.Ordinal);
        Assert.Contains(
            "item.Kind == AppHistoryKind.WindowsGaming",
            historyPage,
            StringComparison.Ordinal);
        Assert.Contains("System.Gaming.RestoreConfirm.Message", historyPage, StringComparison.Ordinal);
        Assert.Contains("System.Gaming.RestoreConfirm.Title", historyPage, StringComparison.Ordinal);
        Assert.Contains(
            ".Where(item => item.Kind == AppHistoryKind.Optimization)",
            historyViewModel,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AccountPlan_ShowsServerEntitlementStatesWithCompleteLocalization()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var appDirectory = Path.Combine(root, "src", "Ralven.App");
        var mainWindow = File.ReadAllText(Path.Combine(appDirectory, "MainWindow.xaml"));
        var accountCode = File.ReadAllText(Path.Combine(appDirectory, "MainWindow.Account.xaml.cs"));

        Assert.Contains("AccountEntitlementValueText", mainWindow, StringComparison.Ordinal);
        Assert.Contains("AccountEntitlementRefresh_Click", mainWindow, StringComparison.Ordinal);
        Assert.Matches(
            "x:Name=\"AccountEntitlementValueText\"[^>]*AutomationProperties.LiveSetting=\"Polite\"",
            mainWindow);
        Assert.Contains("SyncAccountEntitlementAsync", accountCode, StringComparison.Ordinal);
        Assert.Contains("ClearAccountEntitlement", accountCode, StringComparison.Ordinal);

        var keys = new[]
        {
            "Account.Session.ClearFailed",
            "Settings.Account.Plan.Title",
            "Settings.Account.Plan.Free",
            "Settings.Account.Plan.FreeDetail",
            "Settings.Account.Plan.ProUntil",
            "Settings.Account.Plan.ProDetail",
            "Settings.Account.Plan.Unavailable",
            "Settings.Account.Plan.UnavailableDetail",
            "Settings.Account.Plan.Refresh",
        };
        var localizations = new[]
        {
            new LocalizationService(CultureInfo.GetCultureInfo("en-US")),
            new LocalizationService(CultureInfo.GetCultureInfo("pt-BR")),
            new LocalizationService(CultureInfo.GetCultureInfo("es")),
        };

        foreach (var localization in localizations)
        {
            foreach (var key in keys)
            {
                Assert.NotEqual(key, localization.GetString(key));
            }
        }
    }

    [Fact]
    public void AccountPlan_RejectsStaleOrForeignEntitlementResponses()
    {
        var firstUser = new FirebaseUser("uid-1", "first@example.com", true);
        var secondUser = new FirebaseUser("uid-2", "second@example.com", true);

        Assert.True(MainWindow.IsCurrentAccountEntitlementResponse(
            4,
            4,
            firstUser.Uid,
            new AuthenticationSnapshot(AuthenticationState.SignedIn, firstUser)));
        Assert.False(MainWindow.IsCurrentAccountEntitlementResponse(
            3,
            4,
            firstUser.Uid,
            new AuthenticationSnapshot(AuthenticationState.SignedIn, firstUser)));
        Assert.False(MainWindow.IsCurrentAccountEntitlementResponse(
            4,
            4,
            firstUser.Uid,
            new AuthenticationSnapshot(AuthenticationState.SignedIn, secondUser)));
        Assert.False(MainWindow.IsCurrentAccountEntitlementResponse(
            4,
            4,
            firstUser.Uid,
            new AuthenticationSnapshot(AuthenticationState.SignedOut, null)));
    }

    [Fact]
    public void AccountPlan_ProAccessExpiresAtTheServerValidityBoundary()
    {
        var validUntil = DateTimeOffset.Parse(
            "2026-09-30T12:00:00.000Z",
            CultureInfo.InvariantCulture);
        var snapshot = new AccountEntitlementSnapshot(AccountEntitlementTier.Pro, validUntil);

        Assert.True(MainWindow.IsEffectiveProEntitlement(snapshot, validUntil.AddTicks(-1)));
        Assert.False(MainWindow.IsEffectiveProEntitlement(snapshot, validUntil));
        Assert.False(MainWindow.IsEffectiveProEntitlement(
            new AccountEntitlementSnapshot(AccountEntitlementTier.Free),
            validUntil.AddTicks(-1)));
    }

    [Fact]
    public void EveryOptimizationAction_HasLocalizedReviewContent()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var resourceDirectory = Path.Combine(root, "src", "Ralven.App", "Resources");
        var localizedResources = new[]
        {
            "Strings.resx",
            "Strings.pt-BR.resx",
            "Strings.es.resx"
        }.Select(fileName => XDocument
            .Load(Path.Combine(resourceDirectory, fileName))
            .Descendants("data")
            .ToDictionary(
                element => (string)element.Attribute("name")!,
                element => (string?)element.Element("value") ?? string.Empty,
                StringComparer.Ordinal))
            .ToArray();
        var portugueseReviewContent = localizedResources[1];
        var english = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        var portuguese = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        var spanish = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo("es"));

        foreach (var action in ActionCatalog.Current.Actions)
        {
            foreach (var suffix in new[]
                     {
                         "Name",
                         "Description",
                         "DetectionSummary",
                         "ConfirmationSummary",
                         "UndoSummary",
                         "RiskLimitations"
                     })
            {
                var key = $"Actions.{action.Id}.{suffix}";
                Assert.All(localizedResources, resources =>
                {
                    Assert.True(resources.TryGetValue(key, out var value));
                    Assert.False(string.IsNullOrWhiteSpace(value));
                });
                Assert.NotEqual(key, english.GetString(key));
                Assert.NotEqual(key, portuguese.GetString(key));
                Assert.NotEqual(key, spanish.GetString(key));
            }

            Assert.Equal(action.DetectionSummary, portugueseReviewContent[$"Actions.{action.Id}.DetectionSummary"]);
            Assert.Equal(action.ConfirmationSummary, portugueseReviewContent[$"Actions.{action.Id}.ConfirmationSummary"]);
            Assert.Equal(action.UndoSummary, portugueseReviewContent[$"Actions.{action.Id}.UndoSummary"]);
            Assert.Equal(action.RiskLimitations, portugueseReviewContent[$"Actions.{action.Id}.RiskLimitations"]);
        }
    }

    [Fact]
    public void BugReportCodeBehind_LocalizationKeysResolve()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Ralven.App",
            "Views",
            "BugReportWindow.xaml.cs"));
        var keys = LocalizedCodeKeyPattern()
            .Matches(source)
            .Select(match => match.Groups["key"].Value)
            .ToSortedSet(StringComparer.Ordinal);
        var english = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        var portuguese = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        var spanish = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo("es"));

        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            Assert.NotEqual(key, english.GetString(key));
            Assert.NotEqual(key, portuguese.GetString(key));
            Assert.NotEqual(key, spanish.GetString(key));
        }
    }

    [Fact]
    public void BugReportWindow_UsesTheDefinedComboBoxStyle()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var window = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Ralven.App",
            "Views",
            "BugReportWindow.xaml"));
        var controls = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Ralven.App",
            "Themes",
            "Controls.xaml"));

        Assert.Contains("Style=\"{StaticResource SettingsComboBoxStyle}\"", window, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SettingsComboBoxStyle\"", controls, StringComparison.Ordinal);
        Assert.DoesNotContain("FormComboBoxStyle", window, StringComparison.Ordinal);
    }

    [Fact]
    public void PrivacyConsentCodeBehind_LocalizationKeysResolve()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Ralven.App",
            "Views",
            "PrivacyConsentWindow.xaml.cs"));
        var keys = LocalizedCodeKeyPattern()
            .Matches(source)
            .Select(match => match.Groups["key"].Value)
            .ToSortedSet(StringComparer.Ordinal);
        var english = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        var portuguese = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        var spanish = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo("es"));

        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            Assert.NotEqual(key, english.GetString(key));
            Assert.NotEqual(key, portuguese.GetString(key));
            Assert.NotEqual(key, spanish.GetString(key));
        }
    }

    [Fact]
    public void ReleaseNotesCodeBehind_LocalizationKeysResolve()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Ralven.App",
            "Views",
            "ReleaseNotesWindow.xaml.cs"));
        var keys = LocalizedCodeKeyPattern()
            .Matches(source)
            .Select(match => match.Groups["key"].Value)
            .ToSortedSet(StringComparer.Ordinal);
        var english = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        var portuguese = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        var spanish = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo("es"));

        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            Assert.NotEqual(key, english.GetString(key));
            Assert.NotEqual(key, portuguese.GetString(key));
            Assert.NotEqual(key, spanish.GetString(key));
        }
    }

    [Fact]
    public void PrivacyConsentWindow_CanOnlyCloseAfterContinue()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Ralven.App",
            "Views",
            "PrivacyConsentWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Ralven.App",
            "Views",
            "PrivacyConsentWindow.xaml.cs"));

        Assert.DoesNotContain("Click=\"Close_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("e.Cancel = !confirmedByUser;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("confirmedByUser = true;", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Optimizer_SeparatesPreparationProgressAndResults()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var pageDirectory = Path.Combine(root, "src", "Ralven.App", "Views", "Pages");
        var optimizer = File.ReadAllText(Path.Combine(pageDirectory, "OptimizerPage.xaml"))
            + File.ReadAllText(Path.Combine(pageDirectory, "OptimizerPage.xaml.cs"));
        var ultraPanel = File.ReadAllText(Path.Combine(root, "src", "Ralven.App", "Controls", "UltraPanel.xaml"));

        Assert.Contains("IsOptimizerIdle", optimizer, StringComparison.Ordinal);
        Assert.Contains("IsBusy", optimizer, StringComparison.Ordinal);
        Assert.Contains("IsReportAvailable", optimizer, StringComparison.Ordinal);
        Assert.Contains("PlannedActions", optimizer, StringComparison.Ordinal);
        Assert.Contains("GroupedPlannedAdjustments", optimizer, StringComparison.Ordinal);
        Assert.Contains("GroupedInformationalPlanActions", optimizer, StringComparison.Ordinal);
        Assert.Contains("AutomaticAnalysisHeader", optimizer, StringComparison.Ordinal);
        Assert.Contains("IsExpanded=\"False\"", optimizer, StringComparison.Ordinal);
        Assert.Contains("<Expander", optimizer, StringComparison.Ordinal);
        Assert.Contains("DetectionSummary", optimizer, StringComparison.Ordinal);
        Assert.Contains("ConfirmationSummary", optimizer, StringComparison.Ordinal);
        Assert.Contains("UndoSummary", optimizer, StringComparison.Ordinal);
        Assert.Contains("RiskLimitations", optimizer, StringComparison.Ordinal);
        // O trilho único (SpectrumSelector) mantém os quatro perfis no mesmo
        // sistema visual; o sinal de
        // "recomendado" chega via RecommendedIndex, calculado a partir das
        // mesmas três propriedades do ViewModel.
        Assert.Contains("SpectrumSelector", optimizer, StringComparison.Ordinal);
        Assert.Contains("Option3Label=\"{Binding [Ultra.Name]", optimizer, StringComparison.Ordinal);
        Assert.DoesNotContain("ExclusiveLabel=", optimizer, StringComparison.Ordinal);
        Assert.Contains("[Ultra.Exclusive]", ultraPanel, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectUltra_Click", optimizer, StringComparison.Ordinal);
        Assert.Contains("RecommendedIndex", optimizer, StringComparison.Ordinal);
        Assert.Contains("IsLightRecommended", optimizer, StringComparison.Ordinal);
        Assert.Contains("IsBalancedRecommended", optimizer, StringComparison.Ordinal);
        Assert.Contains("IsAggressiveRecommended", optimizer, StringComparison.Ordinal);
        Assert.Contains(": -1;", optimizer, StringComparison.Ordinal);
        // O ledger de execução (StepLedger) agora é exibido de verdade, com
        // marca de resultado por ação, em vez de ficar populado sem uso.
        Assert.Contains("StepLedger", optimizer, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivityLog", optimizer, StringComparison.Ordinal);
        Assert.Contains("Binding ProgressPercent, Mode=OneWay", optimizer, StringComparison.Ordinal);
        Assert.Contains("PreviousProgressHeadline", optimizer, StringComparison.Ordinal);
        Assert.Contains("ProgressHeadline, Mode=OneWay", optimizer, StringComparison.Ordinal);
        Assert.Contains("ElapsedTimeLabel", optimizer, StringComparison.Ordinal);
        Assert.Contains("RemainingTimeLabel", optimizer, StringComparison.Ordinal);
        Assert.Contains("ReportLines", optimizer, StringComparison.Ordinal);
    }

    [Fact]
    public void Dashboard_FocusesOnStatusInsteadOfDuplicatingOptimizerControls()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var dashboard = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Ralven.App",
            "Views",
            "Pages",
            "OverviewPage.xaml"));

        Assert.Contains("StreamingReadinessItems", dashboard, StringComparison.Ordinal);
        Assert.Contains("Dashboard.LivePerformance.Title", dashboard, StringComparison.Ordinal);
        // O histórico ao vivo é um gráfico 2D leve, que recebe as amostras
        // cruas; o medidor de prontidão é um anel animado sobre o núcleo 3D.
        Assert.Contains("controls:LivePerformanceChart", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("PerformanceScene3D", dashboard, StringComparison.Ordinal);
        Assert.Contains("CpuValues=\"{Binding CpuUsageSeries}\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("GpuValues=\"{Binding GpuUsageSeries}\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("NetworkUsageLabel", dashboard, StringComparison.Ordinal);
        Assert.Contains("IsLivePerformanceUnavailable", dashboard, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", dashboard, StringComparison.Ordinal);
        // Redesign "prancheta técnica": a geometria 3D (CoreVisual) e o anel
        // ArcProgress foram removidos do produto por decisão de design — a
        // profundidade passou a vir de camadas, traço e material. A prontidão
        // é lida numa escala graduada, não num medidor decorativo, e o teste
        // trava essa decisão para que nenhum controle 3D volte por descuido.
        Assert.DoesNotContain("controls:CoreVisual", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("controls:ArcProgress", dashboard, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding ReadinessScore, Mode=OneWay}\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding CpuUsagePercent, Mode=OneWay}\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("Dashboard.OpenOptimizer", dashboard, StringComparison.Ordinal);
        Assert.Contains("Dashboard.SystemOverview", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("GroupName=\"Profile\"", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("StartOptimization_Click", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("ProfilePresentationBenefits", dashboard, StringComparison.Ordinal);

        // A faixa de indicadores explica a recomendação com números da própria
        // varredura local, em vez de deixar o espaço vazio abaixo do medidor.
        Assert.Contains("PerformancePressureLabel", dashboard, StringComparison.Ordinal);
        Assert.Contains("LogicalProcessorLabel", dashboard, StringComparison.Ordinal);
        Assert.Contains("AvailableMemoryLabel", dashboard, StringComparison.Ordinal);
        Assert.Contains("LegacyCacheLabel", dashboard, StringComparison.Ordinal);
        // Média e pico saem das mesmas amostras desenhadas no gráfico.
        Assert.Contains("CpuTrendLabel", dashboard, StringComparison.Ordinal);
        Assert.Contains("GpuTrendLabel", dashboard, StringComparison.Ordinal);
        // O fim da página mostra a última execução real, com estado próprio
        // quando ainda não existe histórico.
        Assert.Contains("LastOptimizationTitle", dashboard, StringComparison.Ordinal);
        Assert.Contains("HasLastOptimization", dashboard, StringComparison.Ordinal);
        Assert.Contains("OpenHistory_Click", dashboard, StringComparison.Ordinal);
        // Todos os ícones da Visão geral são vetores do dicionário próprio; a
        // fonte de glifos não é mais usada nesta página.
        Assert.DoesNotContain("Segoe MDL2 Assets", dashboard, StringComparison.Ordinal);

        var viewModel = File.ReadAllText(Path.Combine(root, "src", "Ralven.App", "ViewModels", "MainViewModel.Diagnostics.cs"));
        Assert.Contains("> 75 => localization.GetString(\"Dashboard.Readiness.Excellent\")", viewModel, StringComparison.Ordinal);
        Assert.Contains("> 50 => localization.GetString(\"Dashboard.Readiness.Good\")", viewModel, StringComparison.Ordinal);
        Assert.Contains("> 25 => localization.GetString(\"Dashboard.Readiness.Average\")", viewModel, StringComparison.Ordinal);
        Assert.Contains("> 5 => localization.GetString(\"Dashboard.Readiness.Poor\")", viewModel, StringComparison.Ordinal);
        Assert.Contains("_ => localization.GetString(\"Dashboard.Readiness.VeryPoor\")", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void OperationalPages_PreserveScrollingBusyStateAndNativeProfileSelection()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var appDirectory = Path.Combine(root, "src", "Ralven.App");
        var pageDirectory = Path.Combine(appDirectory, "Views", "Pages");
        var games = File.ReadAllText(Path.Combine(pageDirectory, "GamesPage.xaml"));
        var history = File.ReadAllText(Path.Combine(pageDirectory, "HistoryPage.xaml"));
        var selector = XDocument.Load(Path.Combine(appDirectory, "Controls", "SpectrumSelector.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", games, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", games, StringComparison.Ordinal);
        Assert.Contains("DataContext.IsBusy", history, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled=\"{Binding CanRollback}\"", history, StringComparison.Ordinal);

        var options = selector.Descendants(presentation + "RadioButton").ToArray();
        Assert.Equal(4, options.Length);
        Assert.All(options, option => Assert.Equal("SpectrumOptions", (string?)option.Attribute("GroupName")));
        Assert.Contains(options, option =>
            (string?)option.Attribute(x + "Name") == "Option3Button"
            && option.ToString().Contains("ProAccentGradientBrush", StringComparison.Ordinal));
        Assert.DoesNotContain(selector.Descendants(presentation + "Button"), element =>
            ((string?)element.Attribute(x + "Name"))?.StartsWith("Option", StringComparison.Ordinal) == true);
        var track = Assert.Single(selector.Descendants(presentation + "Grid"), element =>
            (string?)element.Attribute(x + "Name") == "TrackHost");
        Assert.Equal("False", (string?)track.Attribute("Focusable"));
    }

    [Fact]
    public void FluentInteractionStyles_KeepListsStableAndKeyboardFocusVisible()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var styles = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Ralven.App",
            "Themes",
            "Controls.xaml"));
        var typography = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Ralven.App",
            "Themes",
            "Typography.xaml"));
        var pageDirectory = Path.Combine(root, "src", "Ralven.App", "Views", "Pages");
        var overview = File.ReadAllText(Path.Combine(pageDirectory, "OverviewPage.xaml"));
        var optimizer = File.ReadAllText(Path.Combine(pageDirectory, "OptimizerPage.xaml"));

        // ScaleTransform é banido de listas (já causou itens se deslocando sob
        // o ponteiro), mas é uma exceção deliberada e isolada no press do
        // botão primário — nunca dentro de um estilo de linha/lista.
        //
        // A verificação roda sobre a marcação SEM comentários: um comentário
        // que explica por que a exceção existe não é uma ocorrência do
        // recurso, e travar o texto dos comentários proibia justamente
        // documentar a regra ao lado dela.
        var styleMarkup = WithoutXmlComments(styles);
        var primaryButtonStyle = styleMarkup[styleMarkup.IndexOf("x:Key=\"PrimaryButtonStyle\"", StringComparison.Ordinal)..styleMarkup.IndexOf("x:Key=\"SecondaryButtonStyle\"", StringComparison.Ordinal)];
        var stylesOutsidePrimaryButton = styleMarkup.Replace(primaryButtonStyle, string.Empty, StringComparison.Ordinal);
        Assert.Contains("ScaleTransform", primaryButtonStyle, StringComparison.Ordinal);
        // O ContentPresenter herda Foreground do Button. Os estados alteram o
        // Background do próprio controle, que o Border recebe por
        // TemplateBinding; setters no Border deixavam o CTA desabilitado
        // branco e o texto terciário praticamente invisível.
        Assert.DoesNotContain("TextBlock.Foreground=", primaryButtonStyle, StringComparison.Ordinal);
        Assert.DoesNotContain("TargetName=\"Root\" Property=\"Background\"", primaryButtonStyle, StringComparison.Ordinal);
        Assert.Contains("Property=\"Background\" Value=\"{DynamicResource Surface3Brush}\"", primaryButtonStyle, StringComparison.Ordinal);
        Assert.DoesNotContain("ScaleTransform", stylesOutsidePrimaryButton, StringComparison.Ordinal);
        Assert.DoesNotContain("ScaleTransform", WithoutXmlComments(overview), StringComparison.Ordinal);
        Assert.DoesNotContain("ScaleTransform", WithoutXmlComments(optimizer), StringComparison.Ordinal);
        Assert.True(Regex.Matches(styles, "Property=\"IsKeyboardFocused\"").Count >= 3);
        Assert.Contains("<Style TargetType=\"ScrollBar\">", styles, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Right\"", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("DropShadowEffect Color=\"#000000\" BlurRadius=\"5\"", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("Width=\"3\" Height=\"3\"", styles, StringComparison.Ordinal);
        // A fonte oficial é incorporada e declarada uma vez em Typography.xaml;
        // os fallbacks existem apenas para design-time e builds parciais.
        Assert.Contains("/Ralven;component/Assets/Fonts/#Inter", typography, StringComparison.Ordinal);
        Assert.DoesNotContain("DropShadowEffect Color=\"#000000\" BlurRadius=\"10\"", styles, StringComparison.Ordinal);
        // O selo de detecção (FiveM/GTA V) continua com um check vetorial
        // quando detectado e um X quando não, composto inline via DataTrigger
        // em vez de um estilo nomeado dedicado — mas o traçado em si continua
        // vindo do dicionário compartilhado de ícones. Ele existe só na Visão
        // geral: o Otimizador removeu sua cópia duplicada do mesmo selo.
        var icons = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Ralven.App",
            "Themes",
            "Icons.xaml"));

        Assert.Contains("x:Key=\"IconCheck\"", icons, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"IconClose\"", icons, StringComparison.Ordinal);
        Assert.Contains("{StaticResource IconCheck}", overview, StringComparison.Ordinal);
        Assert.Contains("{StaticResource IconClose}", overview, StringComparison.Ordinal);
        Assert.DoesNotContain("{StaticResource IconCheck}", optimizer, StringComparison.Ordinal);
        Assert.DoesNotContain("{StaticResource IconClose}", optimizer, StringComparison.Ordinal);
    }

    [Fact]
    public void ResxCatalogs_HaveNoDuplicateKeys()
    {
        var root = TestHelpers.FindRepositoryRoot();
        foreach (var fileName in new[] { "Strings.resx", "Strings.pt-BR.resx", "Strings.es.resx" })
        {
            var path = Path.Combine(
                root,
                "src",
                "Ralven.App",
                "Resources",
                fileName);
            var document = XDocument.Load(path);
            var duplicateKeys = document
                .Descendants("data")
                .Select(element => (string?)element.Attribute("name"))
                .Where(name => name is not null)
                .GroupBy(name => name!, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();

            Assert.Empty(duplicateKeys);
        }
    }

    [Fact]
    public void PublicBrandName_IsConsistentlyRalven()
    {
        Assert.Equal("Ralven", ProductIdentity.DisplayName);
        Assert.Equal("Ralven", ProductIdentity.Name);

        var root = TestHelpers.FindRepositoryRoot();
        foreach (var fileName in new[] { "Strings.resx", "Strings.pt-BR.resx", "Strings.es.resx" })
        {
            var document = XDocument.Load(Path.Combine(
                root,
                "src",
                "Ralven.App",
                "Resources",
                fileName));
            var values = document.Descendants("value").Select(element => element.Value);

            Assert.Contains(values, value => value == ProductIdentity.DisplayName);
        }

        var oauth = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Ralven.App",
            "Services",
            "GoogleOAuthClient.cs"));
        Assert.Contains("<title>Ralven</title>", oauth, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Ralven\"", oauth, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneralSettings_ExposeAppBehaviorAndPrivacyChoices()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var document = XDocument.Load(
            Path.Combine(root, "src", "Ralven.App", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var checkBoxBindings = document
            .Descendants(presentation + "CheckBox")
            .Select(element => (string?)element.Attribute("IsChecked"))
            .ToArray();

        Assert.Equal(
            new[]
            {
                "{Binding LaunchAtStartup}",
                "{Binding StartMinimized}",
                "{Binding MinimizeToTrayOnClose}",
                "{Binding CheckForUpdates}",
                "{Binding NotifyWhenUpdateAvailable}",
                "{Binding ShareOptionalReports}"
            },
            checkBoxBindings);

        var radioBindings = document
            .Descendants(presentation + "RadioButton")
            .Select(element => (string?)element.Attribute("IsChecked"))
            .Where(value => value is not null)
            .ToArray();

        Assert.DoesNotContain("{Binding IsCloseAppOnCloseSelected, Mode=OneWay}", radioBindings);
        Assert.DoesNotContain("{Binding IsMinimizeToTrayOnCloseSelected, Mode=OneWay}", radioBindings);
    }

    [Fact]
    public void SettingsSelectors_UseThemedControlAndItemTemplates()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(
            root,
            "src",
            "Ralven.App",
            "Themes",
            "Controls.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var selectorStyle = Assert.Single(
            document.Descendants(presentation + "Style"),
            element => (string?)element.Attribute(xaml + "Key") == "SettingsComboBoxStyle");

        Assert.Contains(selectorStyle.Descendants(presentation + "ControlTemplate"), template =>
            (string?)template.Attribute("TargetType") == "ComboBox");
        Assert.Contains(selectorStyle.Descendants(presentation + "Style"), style =>
            (string?)style.Attribute("TargetType") == "ComboBoxItem");
        Assert.Contains(selectorStyle.Descendants(presentation + "Popup"), popup =>
            (string?)popup.Attribute(xaml + "Name") == "PART_Popup");
        Assert.All(
            selectorStyle.Descendants(presentation + "Border")
                .Where(border => border.Attribute("CornerRadius") is not null),
            border => Assert.Equal("{StaticResource RadiusMd}", (string?)border.Attribute("CornerRadius")));
    }

    [Fact]
    public void BugReportAndCopyright_AreInSettingsInsteadOfAGlobalFooter()
    {
        // O rodapé global ("Relatar um bug · © ano") foi removido do shell —
        // as duas informações agora moram em Configurações, perto de onde já
        // fazem sentido (Ferramentas e Sobre), e usam a mesma chave de
        // localização de antes.
        var root = TestHelpers.FindRepositoryRoot();
        var mainWindowPath = Path.Combine(root, "src", "Ralven.App", "MainWindow.xaml");
        var mainWindow = File.ReadAllText(mainWindowPath);
        var document = XDocument.Load(mainWindowPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        Assert.DoesNotContain("Grid.Row=\"2\"", mainWindow, StringComparison.Ordinal);
        Assert.Single(
            document.Descendants(presentation + "Button"),
            element => (string?)element.Attribute("Click") == "ReportBug_Click");
        Assert.Contains(
            document.Descendants(presentation + "TextBlock"),
            element => ((string?)element.Attribute("Text"))?.Contains("Brand.FooterCopyright", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ReleaseNotesLinkButton_UsesLinkButtonStyleInsteadOfTheDefaultButtonChrome()
    {
        // Regression guard: this button previously set Background/BorderThickness
        // manually but kept the default Button ControlTemplate, so WPF still
        // painted its default blue focus/hover chrome around it -- the same
        // bug already fixed once for the "Reportar um bug" link. Using the
        // shared LinkButtonStyle (a bare ContentPresenter template, no focus
        // visual) is what actually removes it.
        var root = TestHelpers.FindRepositoryRoot();
        var document = XDocument.Load(
            Path.Combine(root, "src", "Ralven.App", "Views", "Pages", "OverviewPage.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var releaseNotesButton = Assert.Single(
            document.Descendants(presentation + "Button"),
            element => (string?)element.Attribute("Click") == "OpenReleaseNotes_Click");

        Assert.Equal("{StaticResource LinkButtonStyle}", (string?)releaseNotesButton.Attribute("Style"));
    }

    [Fact]
    public void MainWindow_MaximizesToTheCurrentMonitorWorkArea()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var markup = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Ralven.App",
            "MainWindow.xaml"));
        var source = TestHelpers.ReadMainWindowSource();

        Assert.Contains("WindowState=\"Maximized\"", markup, StringComparison.Ordinal);
        Assert.Contains("WmGetMinMaxInfo", source, StringComparison.Ordinal);
        Assert.Contains("WindowMessageHook", source, StringComparison.Ordinal);
        Assert.Contains("MonitorFromWindow", source, StringComparison.Ordinal);
        Assert.Contains("GetMonitorInfo", source, StringComparison.Ordinal);
        Assert.Contains("minMaxInfo.MaxSize", source, StringComparison.Ordinal);
        Assert.Contains("WindowState = WindowState.Maximized", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LinkButtonStyle_UsesAStableCustomTemplate()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(
            root,
            "src",
            "Ralven.App",
            "Themes",
            "Controls.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var linkStyle = Assert.Single(
            document.Descendants(presentation + "Style"),
            element => (string?)element.Attribute(xaml + "Key") == "LinkButtonStyle");

        Assert.Contains(linkStyle.Descendants(presentation + "ControlTemplate"), template =>
            (string?)template.Attribute("TargetType") == "Button");
        // O redesign acrescentou feedback de hover (opacidade reduzida) a
        // este botão — toda microinteração do app precisa reagir a
        // hover/pressed/focused, e um link sem nenhum dos três não cumpria
        // essa exigência.
        Assert.Contains(linkStyle.Descendants(presentation + "Trigger"), trigger =>
            (string?)trigger.Attribute("Property") == "IsMouseOver");
        Assert.Contains(linkStyle.Descendants(presentation + "Trigger"), trigger =>
            (string?)trigger.Attribute("Property") == "IsKeyboardFocused");
    }

    [Fact]
    public void SettingsAndWindowChrome_UseTheRefinedSpacingAndHoverContracts()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Ralven.App",
            "MainWindow.xaml"));
        var controls = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Ralven.App",
            "Themes",
            "Controls.xaml"));

        Assert.Contains("ToolTip=\"{Binding [Safety.SnapshotRollback]", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding [Safety.SnapshotRollback]", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding [Settings.Subtitle]", mainWindow, StringComparison.Ordinal);
        Assert.Contains("<ui:TitleBar", mainWindow, StringComparison.Ordinal);

        // O seletor reserva folga à direita para o chevron: sem ela o valor
        // selecionado passa por baixo da seta em idiomas de rótulo longo. O
        // teste trava a REGRA (folga direita > folga esquerda, e o suficiente
        // para o glifo), não um valor de padding específico, que muda sempre
        // que a altura do controle é reajustada.
        var comboPadding = ThicknessOf(SettingsComboBoxPadding(controls));
        Assert.True(
            comboPadding.Right >= 30,
            $"SettingsComboBoxStyle reserva apenas {comboPadding.Right}px à direita; o chevron precisa de pelo menos 30.");
        Assert.True(
            comboPadding.Right > comboPadding.Left,
            "SettingsComboBoxStyle precisa de mais folga à direita que à esquerda: a seta mora naquele lado.");

        Assert.Contains("Content=\"{Binding SelectedValue, RelativeSource={RelativeSource AncestorType=ComboBox}}\"", controls, StringComparison.Ordinal);
        Assert.Contains("SelectedValuePath=\"Content\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Icon=\"{ui:SymbolIcon Shield24}\"", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("&#xEA18;", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void SupportCard_AlignsItsStatusAndShowsTheInstalledVersion()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Ralven.App",
            "MainWindow.xaml"));
        var controls = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Ralven.App",
            "Themes",
            "Controls.xaml"));

        Assert.Contains("VerticalAlignment=\"Center\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Icon=\"{ui:SymbolIcon Shield24}\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding [Sidebar.Version], Source={StaticResource LocalizedStrings}, Mode=OneWay}\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding AppVersion, Mode=OneWay}\"", mainWindow, StringComparison.Ordinal);
        // A hierarquia de texto agora vem da escala tipográfica (Overline/
        // Caption/Body), não de um Foreground fixo por elemento.
        Assert.Contains("Style=\"{StaticResource CaptionText}\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Padding=\"{TemplateBinding Padding}\"", controls, StringComparison.Ordinal);
    }

    /// <summary>
    /// Remove comentários XML da marcação antes de uma verificação textual.
    /// Sem isto, um comentário que explica por que uma regra existe conta
    /// como violação dela — o que na prática proíbe documentar a decisão
    /// junto do código que a implementa.
    /// </summary>
    private static string WithoutXmlComments(string markup)
    {
        return XmlCommentPattern().Replace(markup, string.Empty);
    }

    /// <summary>
    /// Extrai o valor de <c>Padding</c> declarado por <c>SettingsComboBoxStyle</c>
    /// em <c>Themes/Controls.xaml</c>.
    /// </summary>
    private static string SettingsComboBoxPadding(string controls)
    {
        var document = XDocument.Parse(controls);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var style = Assert.Single(
            document.Descendants(presentation + "Style"),
            element => (string?)element.Attribute(xaml + "Key") == "SettingsComboBoxStyle");
        var padding = style
            .Elements(presentation + "Setter")
            .FirstOrDefault(setter => (string?)setter.Attribute("Property") == "Padding");

        Assert.NotNull(padding);
        var value = (string?)padding!.Attribute("Value");
        Assert.NotNull(value);
        return value!;
    }

    /// <summary>Interpreta um <c>Thickness</c> XAML de quatro componentes.</summary>
    private static (double Left, double Top, double Right, double Bottom) ThicknessOf(string value)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        Assert.Equal(4, parts.Length);
        var numbers = parts
            .Select(part => double.Parse(part, CultureInfo.InvariantCulture))
            .ToArray();
        return (numbers[0], numbers[1], numbers[2], numbers[3]);
    }

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex XmlCommentPattern();

    [GeneratedRegex(@"<Run\b[^>]*\bText=""\{Binding \[[^]]+\], Source=\{StaticResource LocalizedStrings\}(?![^""]*\bMode=OneWay)", RegexOptions.CultureInvariant)]
    private static partial Regex LocalizedRunWithoutOneWayPattern();

    [GeneratedRegex(@"\[\s*(?<key>[A-Za-z0-9_.-]+)\s*\]", RegexOptions.CultureInvariant)]
    private static partial Regex LocalizedKeyPattern();

    [GeneratedRegex(@"\b(?:T|F)\(""(?<key>[A-Za-z0-9_.-]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex LocalizedCodeKeyPattern();
}
