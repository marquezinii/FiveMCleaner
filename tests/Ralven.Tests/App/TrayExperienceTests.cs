using Ralven.App.Services;
using Xunit;

namespace Ralven.Tests.App;

public sealed class TrayExperienceTests
{
    [Fact]
    public void TrayMenu_KeepsTheNativeHostAndDelegatesPresentationToWpf()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Ralven.App",
            "Services",
            "TrayIconService.cs"));
        var shell = File.ReadAllText(Path.Combine(root, "src", "Ralven.App", "MainWindow.xaml"));
        var tray = File.ReadAllText(Path.Combine(root, "src", "Ralven.App", "MainWindow.Tray.xaml.cs"));
        var manifest = File.ReadAllText(Path.Combine(root, "src", "Ralven.App", "app.manifest"));

        Assert.Contains("Forms.NotifyIcon", service, StringComparison.Ordinal);
        Assert.Contains("nativeMenuBridge.Opening += NativeMenuBridge_Opening", service, StringComparison.Ordinal);
        Assert.Contains("notifyIcon.MouseClick +=", service, StringComparison.Ordinal);
        Assert.Contains("notifyIcon.MouseDoubleClick +=", service, StringComparison.Ordinal);
        Assert.Contains("e.Cancel = true", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolStripMenuItem", service, StringComparison.Ordinal);

        Assert.Contains("x:Key=\"TrayContextMenu\"", shell, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource TrayContextMenuStyle}\"", shell, StringComparison.Ordinal);
        Assert.Contains("Placement=\"MousePoint\"", shell, StringComparison.Ordinal);
        Assert.Contains("StaysOpen=\"False\"", shell, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name", shell, StringComparison.Ordinal);
        Assert.Contains("TraySessionMonitor_Click", tray, StringComparison.Ordinal);
        Assert.Contains("TrayCheckUpdates_Click", tray, StringComparison.Ordinal);
        Assert.Contains("TraySettings_Click", tray, StringComparison.Ordinal);
        Assert.Contains("PerMonitorV2", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeToolTip_RespectsTheNotifyIconLimitWithoutSplittingSurrogates()
    {
        var exact = new string('a', 127);
        var withSurrogateAtBoundary = new string('a', 126) + "😀";

        Assert.Same(exact, TrayIconService.NormalizeToolTip(exact));
        Assert.Equal(new string('a', 126), TrayIconService.NormalizeToolTip(withSurrogateAtBoundary));
        Assert.Equal(127, TrayIconService.NormalizeToolTip(new string('b', 140)).Length);
    }
}
