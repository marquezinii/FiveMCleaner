using System.Text.RegularExpressions;
using System.Xml.Linq;
using FiveMCleaner.App.Services;
using FiveMCleaner.Core.Catalog;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed partial class LocalizedInterfaceContractTests
{
    [Fact]
    public void LocalizedXamlBindings_ResolveInEnglishAndPortuguese()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var sources = new[]
        {
            Path.Combine(root, "src", "FiveMCleaner.App", "MainWindow.xaml"),
            Path.Combine(root, "src", "FiveMCleaner.App", "Views", "BugReportWindow.xaml"),
            Path.Combine(root, "src", "FiveMCleaner.App", "Views", "PrivacyConsentWindow.xaml"),
            Path.Combine(root, "src", "FiveMCleaner.App", "Views", "ReleaseNotesWindow.xaml")
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
    public void EveryOptimizationAction_HasLocalizedNameAndDescription()
    {
        var english = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        var portuguese = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        var spanish = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo("es"));

        foreach (var action in ActionCatalog.Current.Actions)
        {
            foreach (var suffix in new[] { "Name", "Description" })
            {
                var key = $"Actions.{action.Id}.{suffix}";
                Assert.NotEqual(key, english.GetString(key));
                Assert.NotEqual(key, portuguese.GetString(key));
                Assert.NotEqual(key, spanish.GetString(key));
            }
        }
    }

    [Fact]
    public void BugReportCodeBehind_LocalizationKeysResolve()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FiveMCleaner.App",
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
    public void PrivacyConsentCodeBehind_LocalizationKeysResolve()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FiveMCleaner.App",
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
            "FiveMCleaner.App",
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
            "FiveMCleaner.App",
            "Views",
            "PrivacyConsentWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FiveMCleaner.App",
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
        var pageDirectory = Path.Combine(root, "src", "FiveMCleaner.App", "Views", "Pages");
        var optimizer = File.ReadAllText(Path.Combine(pageDirectory, "OptimizerPage.xaml"))
            + File.ReadAllText(Path.Combine(pageDirectory, "OptimizerPage.xaml.cs"));

        Assert.Contains("IsOptimizerIdle", optimizer, StringComparison.Ordinal);
        Assert.Contains("IsBusy", optimizer, StringComparison.Ordinal);
        Assert.Contains("IsReportAvailable", optimizer, StringComparison.Ordinal);
        Assert.Contains("PlannedActions", optimizer, StringComparison.Ordinal);
        // O trilho único (SpectrumSelector) substituiu o hero recomendado +
        // três cards de perfil por um único sistema visual; o sinal de
        // "recomendado" chega via RecommendedIndex, calculado a partir das
        // mesmas três propriedades do ViewModel.
        Assert.Contains("SpectrumSelector", optimizer, StringComparison.Ordinal);
        Assert.Contains("RecommendedIndex", optimizer, StringComparison.Ordinal);
        Assert.Contains("IsLightRecommended", optimizer, StringComparison.Ordinal);
        Assert.Contains("IsBalancedRecommended", optimizer, StringComparison.Ordinal);
        Assert.Contains("IsAggressiveRecommended", optimizer, StringComparison.Ordinal);
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
            "FiveMCleaner.App",
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
        // HoloCore3D e OptimizerCore3D se fundiram num único CoreVisual
        // (Mode=Readiness|Engine) — o mesmo objeto em toda a interface.
        Assert.Contains("controls:CoreVisual", dashboard, StringComparison.Ordinal);
        Assert.Contains("Mode=\"Readiness\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding ReadinessScore, Mode=OneWay}\"", dashboard, StringComparison.Ordinal);
        // Redesign "Bancada de tuning premium": o aro do medidor de prontidão
        // usa o bezel de metal escovado (InstrumentBezelBrush), não mais a
        // borda genérica de superfície.
        Assert.Contains("TrackBrush=\"{DynamicResource InstrumentBezelBrush}\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding CpuUsagePercent, Mode=OneWay}\"", dashboard, StringComparison.Ordinal);
        // A coleta pausada precisa pausar também a animação da cena.
        Assert.Contains("IsLive=\"{Binding IsLiveMetricsActive}\"", dashboard, StringComparison.Ordinal);
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

        var viewModel = File.ReadAllText(Path.Combine(root, "src", "FiveMCleaner.App", "ViewModels", "MainViewModel.cs"));
        Assert.Contains("> 75 => localization.GetString(\"Dashboard.Readiness.Excellent\")", viewModel, StringComparison.Ordinal);
        Assert.Contains("> 50 => localization.GetString(\"Dashboard.Readiness.Good\")", viewModel, StringComparison.Ordinal);
        Assert.Contains("> 25 => localization.GetString(\"Dashboard.Readiness.Average\")", viewModel, StringComparison.Ordinal);
        Assert.Contains("> 5 => localization.GetString(\"Dashboard.Readiness.Poor\")", viewModel, StringComparison.Ordinal);
        Assert.Contains("_ => localization.GetString(\"Dashboard.Readiness.VeryPoor\")", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void FluentInteractionStyles_KeepListsStableAndKeyboardFocusVisible()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var styles = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FiveMCleaner.App",
            "Themes",
            "Controls.xaml"));
        var typography = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FiveMCleaner.App",
            "Themes",
            "Typography.xaml"));
        var pageDirectory = Path.Combine(root, "src", "FiveMCleaner.App", "Views", "Pages");
        var overview = File.ReadAllText(Path.Combine(pageDirectory, "OverviewPage.xaml"));
        var optimizer = File.ReadAllText(Path.Combine(pageDirectory, "OptimizerPage.xaml"));

        // ScaleTransform é banido de listas (já causou itens se deslocando sob
        // o ponteiro), mas é uma exceção deliberada e isolada no press do
        // botão primário — nunca dentro de um estilo de linha/lista.
        var primaryButtonStyle = styles[styles.IndexOf("x:Key=\"PrimaryButtonStyle\"", StringComparison.Ordinal)..styles.IndexOf("x:Key=\"SecondaryButtonStyle\"", StringComparison.Ordinal)];
        var stylesOutsidePrimaryButton = styles.Replace(primaryButtonStyle, string.Empty, StringComparison.Ordinal);
        Assert.Contains("ScaleTransform", primaryButtonStyle, StringComparison.Ordinal);
        Assert.DoesNotContain("ScaleTransform", stylesOutsidePrimaryButton, StringComparison.Ordinal);
        Assert.DoesNotContain("ScaleTransform", overview, StringComparison.Ordinal);
        Assert.DoesNotContain("ScaleTransform", optimizer, StringComparison.Ordinal);
        Assert.True(Regex.Matches(styles, "Property=\"IsKeyboardFocused\"").Count >= 3);
        Assert.Contains("<Style TargetType=\"ScrollBar\">", styles, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Right\"", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("DropShadowEffect Color=\"#000000\" BlurRadius=\"5\"", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("Width=\"3\" Height=\"3\"", styles, StringComparison.Ordinal);
        // A fonte variável do Fluent é declarada uma vez, em Typography.xaml,
        // e consumida por todos os estilos nomeados — não mais espalhada.
        Assert.Contains("Segoe UI Variable Text, Segoe UI", typography, StringComparison.Ordinal);
        Assert.DoesNotContain("DropShadowEffect Color=\"#000000\" BlurRadius=\"10\"", styles, StringComparison.Ordinal);
        // O selo de detecção (FiveM/GTA V) continua com um check vetorial
        // quando detectado e um X quando não, composto inline via DataTrigger
        // em vez de um estilo nomeado dedicado — mas o traçado em si continua
        // vindo do dicionário compartilhado de ícones. Ele existe só na Visão
        // geral: o Otimizador removeu sua cópia duplicada do mesmo selo.
        var icons = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FiveMCleaner.App",
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
                "FiveMCleaner.App",
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
    public void GeneralSettings_ExposeOnlyAppBehaviorChoices()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var document = XDocument.Load(
            Path.Combine(root, "src", "FiveMCleaner.App", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var checkBoxBindings = document
            .Descendants(presentation + "CheckBox")
            .Select(element => (string?)element.Attribute("IsChecked"))
            .ToArray();

        Assert.Equal(
            new[]
            {
                "{Binding MinimizeToTrayOnClose}",
                "{Binding LaunchAtStartup}",
                "{Binding ShareAnonymousTelemetry}"
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
            "FiveMCleaner.App",
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
        var mainWindowPath = Path.Combine(root, "src", "FiveMCleaner.App", "MainWindow.xaml");
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
            Path.Combine(root, "src", "FiveMCleaner.App", "Views", "Pages", "OverviewPage.xaml"));
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
            "FiveMCleaner.App",
            "MainWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FiveMCleaner.App",
            "MainWindow.xaml.cs"));

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
            "FiveMCleaner.App",
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
            "FiveMCleaner.App",
            "MainWindow.xaml"));
        var controls = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FiveMCleaner.App",
            "Themes",
            "Controls.xaml"));

        Assert.Contains("ToolTip=\"{Binding [Safety.SnapshotRollback]", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding [Safety.SnapshotRollback]", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding [Settings.Subtitle]", mainWindow, StringComparison.Ordinal);
        Assert.Contains("<ui:TitleBar", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Padding\" Value=\"12,0,32,0\"", controls, StringComparison.Ordinal);
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
            "FiveMCleaner.App",
            "MainWindow.xaml"));
        var controls = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FiveMCleaner.App",
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

    [GeneratedRegex(@"\[\s*(?<key>[A-Za-z0-9_.-]+)\s*\]", RegexOptions.CultureInvariant)]
    private static partial Regex LocalizedKeyPattern();

    [GeneratedRegex(@"\b(?:T|F)\(""(?<key>[A-Za-z0-9_.-]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex LocalizedCodeKeyPattern();
}
