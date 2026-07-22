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
            Path.Combine(root, "src", "FiveMCleaner.App", "Views", "BugReportWindow.xaml")
        };
        var keys = sources
            .SelectMany(path => LocalizedKeyPattern().Matches(File.ReadAllText(path)))
            .Select(match => match.Groups["key"].Value)
            .ToSortedSet(StringComparer.Ordinal);
        var english = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        var portuguese = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            Assert.NotEqual(key, english.GetString(key));
            Assert.NotEqual(key, portuguese.GetString(key));
        }
    }

    [Fact]
    public void EveryOptimizationAction_HasLocalizedNameAndDescription()
    {
        var english = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        var portuguese = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

        foreach (var action in ActionCatalog.Current.Actions)
        {
            foreach (var suffix in new[] { "Name", "Description" })
            {
                var key = $"Actions.{action.Id}.{suffix}";
                Assert.NotEqual(key, english.GetString(key));
                Assert.NotEqual(key, portuguese.GetString(key));
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

        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            Assert.NotEqual(key, english.GetString(key));
            Assert.NotEqual(key, portuguese.GetString(key));
        }
    }

    [Fact]
    public void ResxCatalogs_HaveNoDuplicateKeys()
    {
        var root = FindRepositoryRoot();
        foreach (var fileName in new[] { "Strings.resx", "Strings.pt-BR.resx" })
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
                "{Binding LaunchAtStartup}"
            },
            checkBoxBindings);

        var radioBindings = document
            .Descendants(presentation + "RadioButton")
            .Select(element => (string?)element.Attribute("IsChecked"))
            .Where(value => value is not null)
            .ToArray();

        Assert.Contains("{Binding IsCloseAppOnCloseSelected, Mode=OneWay}", radioBindings);
        Assert.Contains("{Binding IsMinimizeToTrayOnCloseSelected, Mode=OneWay}", radioBindings);
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
