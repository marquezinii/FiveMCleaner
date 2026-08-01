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
        var root = FindRepositoryRoot();
        var sources = new[]
        {
            Path.Combine(root, "src", "FiveMCleaner.App", "MainWindow.xaml"),
            Path.Combine(root, "src", "FiveMCleaner.App", "Views", "BugReportWindow.xaml"),
            Path.Combine(root, "src", "FiveMCleaner.App", "Views", "PrivacyConsentWindow.xaml")
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
        var root = FindRepositoryRoot();
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
        var root = FindRepositoryRoot();
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
    public void PrivacyConsentWindow_CanOnlyCloseAfterContinue()
    {
        var root = FindRepositoryRoot();
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
    public void Optimizer_UsesACompactProgressTimelineInsteadOfThePlanAndLedgerLists()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FiveMCleaner.App",
            "MainWindow.xaml"));
        var optimizer = source[source.IndexOf("<!-- Optimizer -->", StringComparison.Ordinal)..source.IndexOf("<!-- History -->", StringComparison.Ordinal)];

        Assert.DoesNotContain("PlannedActions", optimizer, StringComparison.Ordinal);
        Assert.DoesNotContain("StepLedger", optimizer, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivityLog", optimizer, StringComparison.Ordinal);
        Assert.Contains("ProgressBar Value=\"{Binding ProgressPercent", optimizer, StringComparison.Ordinal);
        Assert.Contains("PreviousProgressHeadline", optimizer, StringComparison.Ordinal);
        Assert.Contains("ProgressHeadline, Mode=OneWay", optimizer, StringComparison.Ordinal);
        Assert.Contains("ElapsedTimeLabel", optimizer, StringComparison.Ordinal);
        Assert.Contains("RemainingTimeLabel", optimizer, StringComparison.Ordinal);
    }

    [Fact]
    public void FluentInteractionStyles_KeepListsStableAndKeyboardFocusVisible()
    {
        var root = FindRepositoryRoot();
        var styles = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FiveMCleaner.App",
            "Themes",
            "Controls.xaml"));
        var mainWindow = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FiveMCleaner.App",
            "MainWindow.xaml"));

        Assert.DoesNotContain("ScaleTransform", styles, StringComparison.Ordinal);
        Assert.True(Regex.Matches(styles, "Property=\"IsKeyboardFocused\"").Count >= 3);
        Assert.Contains("<Style TargetType=\"ScrollBar\">", styles, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Right\"", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("DropShadowEffect Color=\"#000000\" BlurRadius=\"5\"", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("Width=\"3\" Height=\"3\"", styles, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"DetectionBadgeStyle\"", styles, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"DetectionBadgeLabelStyle\"", styles, StringComparison.Ordinal);
        Assert.Contains("Segoe UI Variable Text, Segoe UI", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("DropShadowEffect Color=\"#000000\" BlurRadius=\"10\"", styles, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(mainWindow, "M 2.5,7 L 5.5,10 L 11.5,4.5").Count);
    }

    [Fact]
    public void ResxCatalogs_HaveNoDuplicateKeys()
    {
        var root = FindRepositoryRoot();
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
        var root = FindRepositoryRoot();
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
                "{Binding ShareAnonymousTelemetry}",
                "{Binding ShareCrashReports}"
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
    public void ReadinessRing_IsATrueCircle()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(
            Path.Combine(root, "src", "FiveMCleaner.App", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var ring = Assert.Single(
            document.Descendants(presentation + "Ellipse"),
            element => ((string?)element.Attribute("Stroke"))?.Contains(
                "RingBrush",
                StringComparison.Ordinal) == true);

        Assert.Equal((string?)ring.Attribute("Width"), (string?)ring.Attribute("Height"));
        Assert.Equal("Uniform", (string?)ring.Attribute("Stretch"));
    }

    [Fact]
    public void SettingsSelectors_UseThemedControlAndItemTemplates()
    {
        var root = FindRepositoryRoot();
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
            border => Assert.Equal("0", (string?)border.Attribute("CornerRadius")));
    }

    [Fact]
    public void BugReportAndCopyright_AreInTheGlobalFooter()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(
            Path.Combine(root, "src", "FiveMCleaner.App", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var reportButton = Assert.Single(
            document.Descendants(presentation + "Button"),
            element => (string?)element.Attribute("Click") == "ReportBug_Click");
        var footer = reportButton.Ancestors(presentation + "Border").FirstOrDefault();

        Assert.NotNull(footer);
        Assert.Equal("2", (string?)footer!.Attribute("Grid.Row"));
        Assert.Contains(
            footer.Descendants(presentation + "TextBlock"),
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
        var root = FindRepositoryRoot();
        var document = XDocument.Load(
            Path.Combine(root, "src", "FiveMCleaner.App", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var releaseNotesButton = Assert.Single(
            document.Descendants(presentation + "Button"),
            element => (string?)element.Attribute("Click") == "OpenReleaseNotes_Click");

        Assert.Equal("{StaticResource LinkButtonStyle}", (string?)releaseNotesButton.Attribute("Style"));
    }

    [Fact]
    public void MainWindow_MaximizesToTheCurrentMonitorWorkArea()
    {
        var root = FindRepositoryRoot();
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
        var root = FindRepositoryRoot();
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
        Assert.DoesNotContain(linkStyle.Descendants(presentation + "Trigger"), trigger =>
            (string?)trigger.Attribute("Property") == "IsMouseOver");
    }

    [Fact]
    public void SettingsAndWindowChrome_UseTheRefinedSpacingAndHoverContracts()
    {
        var root = FindRepositoryRoot();
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
        var root = FindRepositoryRoot();
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
        Assert.Contains("Text=\"{Binding [Sidebar.Version], Source={StaticResource LocalizedStrings}}\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding AppVersion, Mode=OneWay}\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{DynamicResource TextBrush}\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Padding=\"{TemplateBinding Padding}\"", controls, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FiveMCleaner.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("FiveMCleaner repository root was not found.");
    }

    [GeneratedRegex(@"\[\s*(?<key>[A-Za-z0-9_.-]+)\s*\]", RegexOptions.CultureInvariant)]
    private static partial Regex LocalizedKeyPattern();

    [GeneratedRegex(@"\b(?:T|F)\(""(?<key>[A-Za-z0-9_.-]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex LocalizedCodeKeyPattern();
}
