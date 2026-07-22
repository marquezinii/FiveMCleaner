using FiveMCleaner.Windows.Infrastructure;
using Xunit;

namespace FiveMCleaner.Tests.Windows;

public sealed class GtaVInfrastructureTests
{
    [Theory]
    [InlineData("GTA5", true)]
    [InlineData("gta5", true)]
    [InlineData("PlayGTAV", true)]
    [InlineData("GTAVLauncher", true)]
    [InlineData("GTA5_BE", true)]
    [InlineData("GTA5_b3258", false)]
    [InlineData("MyGTAVLauncher", false)]
    [InlineData("FiveM", false)]
    [InlineData("", false)]
    public void ProcessNameFallback_OnlyAcceptsKnownGtaVExecutables(
        string processName,
        bool expected)
    {
        Assert.Equal(
            expected,
            WindowsGtaVProcessInspector.LooksLikeGtaVProcessName(processName));
    }

    [Fact]
    public void ReadIvPath_ParsesCaseInsensitiveQuotedValue()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var iniPath = temporaryDirectory.Combine("CitizenFX.ini");
        var expected = temporaryDirectory.Combine("Grand Theft Auto V");
        File.WriteAllText(
            iniPath,
            $"UpdateChannel=production{Environment.NewLine}ivpath = \"{expected}\"{Environment.NewLine}");

        var detected = GtaVLocator.ReadIvPath(iniPath);

        Assert.Equal(expected, detected);
    }

    [Fact]
    public void ReadIvPath_IgnoresOversizedConfigurationFile()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var iniPath = temporaryDirectory.Combine("CitizenFX.ini");
        using (var stream = new FileStream(iniPath, FileMode.CreateNew, FileAccess.Write))
        {
            stream.SetLength((1024 * 1024) + 1);
        }

        Assert.Null(GtaVLocator.ReadIvPath(iniPath));
    }

    [Fact]
    public void Detect_UsesCitizenFxIvPathAndBuildsCanonicalPaths()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var fiveMRoot = temporaryDirectory.Combine("FiveM");
        var fiveMAppRoot = Path.Combine(fiveMRoot, "FiveM.app");
        var gtaVRoot = temporaryDirectory.Combine("Games", "Grand Theft Auto V");
        Directory.CreateDirectory(fiveMAppRoot);
        Directory.CreateDirectory(gtaVRoot);
        File.WriteAllText(Path.Combine(gtaVRoot, "GTA5.exe"), string.Empty);
        File.WriteAllText(
            Path.Combine(fiveMAppRoot, "CitizenFX.ini"),
            $"IVPath=\"{gtaVRoot}\"{Environment.NewLine}");

        var detected = GtaVLocator.Detect(fiveMRoot);

        Assert.True(detected.IsInstalled);
        Assert.Equal(Path.GetFullPath(gtaVRoot), detected.InstallationRoot, ignoreCase: true);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(gtaVRoot, "GTA5.exe")),
            detected.ExecutablePath,
            ignoreCase: true);
        Assert.Equal(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Rockstar Games",
                "GTA V",
                "settings.xml"),
            detected.GraphicsSettingsPath,
            ignoreCase: true);
    }
}
