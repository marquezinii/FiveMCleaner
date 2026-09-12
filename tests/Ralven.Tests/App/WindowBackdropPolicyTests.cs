using Ralven.App.Services;
using Wpf.Ui.Controls;
using Xunit;

namespace Ralven.Tests.App;

public sealed class WindowBackdropPolicyTests
{
    [Fact]
    public void FluentWindows_UseTheAdaptiveBackdropPolicy()
    {
        var appDirectory = Path.Combine(TestHelpers.FindRepositoryRoot(), "src", "Ralven.App");
        var windows = Directory
            .EnumerateFiles(appDirectory, "*.xaml", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .Where(markup => markup.Contains("WindowBackdropType=", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(windows);
        Assert.All(windows, markup => Assert.Contains(
            "WindowBackdropType=\"{x:Static services:WindowBackdropPolicy.Current}\"",
            markup,
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(19041)]
    [InlineData(21999)]
    public void SelectForBuild_UsesAcrylicOnWindows10(int build)
    {
        Assert.Equal(WindowBackdropType.Acrylic, WindowBackdropPolicy.SelectForBuild(build));
    }

    [Theory]
    [InlineData(22000)]
    [InlineData(26100)]
    public void SelectForBuild_PreservesMicaOnWindows11(int build)
    {
        Assert.Equal(WindowBackdropType.Mica, WindowBackdropPolicy.SelectForBuild(build));
    }
}
