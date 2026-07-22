using System.Xml.Linq;
using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Windows.Actions;
using Xunit;

namespace FiveMCleaner.Tests.Windows;

public sealed class GtaVGraphicsPresetTests
{
    [Theory]
    [InlineData(OptimizationProfile.Light, OptimizationActionIds.ApplyLightGtaVGraphics)]
    [InlineData(OptimizationProfile.Balanced, OptimizationActionIds.ApplyBalancedGtaVGraphics)]
    [InlineData(OptimizationProfile.Aggressive, OptimizationActionIds.ApplyAggressiveGtaVGraphics)]
    public void GtaVGraphicsAction_UsesProfileSpecificMetadata(
        OptimizationProfile profile,
        string expectedActionId)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var action = new LegacyGraphicsPresetAction(
            temporaryDirectory.Combine("Rockstar Games", "GTA V", "settings.xml"),
            temporaryDirectory.Combine("Grand Theft Auto V"),
            profile,
            GraphicsSettingsTarget.GtaV,
            new FakeProcessInspector(),
            new FakeGtaVProcessInspector());

        Assert.Equal(expectedActionId, action.Metadata.Id);
        Assert.Equal([profile], action.Metadata.SupportedProfiles);
    }

    [Fact]
    public async Task AggressiveGtaVGraphics_OnlyLowersAllowlistedValuesAndRollsBackExactly()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var gtaVRoot = temporaryDirectory.Combine("Grand Theft Auto V");
        var settingsPath = temporaryDirectory.Combine(
            "Documents",
            "Rockstar Games",
            "GTA V",
            "settings.xml");
        Directory.CreateDirectory(gtaVRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        const string original =
            "<Settings><graphics>"
            + "<ShadowQuality value=\"3\"/>"
            + "<TextureQuality value=\"0\"/>"
            + "<MSAA value=\"4\"/>"
            + "<ScreenWidth value=\"1920\"/>"
            + "<ScreenHeight value=\"1080\"/>"
            + "<Windowed value=\"0\"/>"
            + "<AdapterIndex value=\"1\"/>"
            + "<DX_Version value=\"2\"/>"
            + "<UnknownCost value=\"9\"/>"
            + "</graphics></Settings>";
        File.WriteAllText(settingsPath, original);
        var fiveMInspector = new FakeProcessInspector();
        var gtaVInspector = new FakeGtaVProcessInspector();
        var action = new LegacyGraphicsPresetAction(
            settingsPath,
            gtaVRoot,
            OptimizationProfile.Aggressive,
            GraphicsSettingsTarget.GtaV,
            fiveMInspector,
            gtaVInspector);
        var context = Context();

        var result = await action.ApplyAsync(context, CancellationToken.None);

        Assert.True(result.Changed);
        var document = XDocument.Load(settingsPath);
        Assert.Equal("1", Value(document, "ShadowQuality"));
        Assert.Equal("0", Value(document, "TextureQuality"));
        Assert.Equal("0", Value(document, "MSAA"));
        Assert.Equal("1920", Value(document, "ScreenWidth"));
        Assert.Equal("1080", Value(document, "ScreenHeight"));
        Assert.Equal("0", Value(document, "Windowed"));
        Assert.Equal("1", Value(document, "AdapterIndex"));
        Assert.Equal("2", Value(document, "DX_Version"));
        Assert.Equal("9", Value(document, "UnknownCost"));
        Assert.Null(document.Descendants("ReflectionQuality").SingleOrDefault());
        Assert.Equal(0, fiveMInspector.CallCount);
        Assert.Equal(2, gtaVInspector.CallCount);
        Assert.Equal(Path.GetFullPath(gtaVRoot), gtaVInspector.LastInstallationRoot, ignoreCase: true);

        await action.RollbackAsync(context, result.SnapshotJson, CancellationToken.None);

        Assert.Equal(original, File.ReadAllText(settingsPath));
        Assert.Equal(4, gtaVInspector.CallCount);
    }

    [Fact]
    public async Task GtaVGraphics_RefusesToWriteWhileGtaVIsRunning()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var gtaVRoot = temporaryDirectory.Combine("Grand Theft Auto V");
        var settingsPath = temporaryDirectory.Combine("GTA V", "settings.xml");
        Directory.CreateDirectory(gtaVRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        const string original = "<Settings><graphics><MSAA value=\"4\"/></graphics></Settings>";
        File.WriteAllText(settingsPath, original);
        var fiveMInspector = new FakeProcessInspector();
        var gtaVInspector = new FakeGtaVProcessInspector(running: true);
        var action = new LegacyGraphicsPresetAction(
            settingsPath,
            gtaVRoot,
            OptimizationProfile.Light,
            GraphicsSettingsTarget.GtaV,
            fiveMInspector,
            gtaVInspector);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            action.ApplyAsync(Context(), CancellationToken.None));

        Assert.Contains("GTA V", exception.Message, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllText(settingsPath));
        Assert.Equal(0, fiveMInspector.CallCount);
        Assert.Equal(1, gtaVInspector.CallCount);
    }

    [Theory]
    [InlineData("<Settings><graphics><ShadowQuality value=\"3\"/><ShadowQuality value=\"2\"/></graphics></Settings>")]
    [InlineData("<Settings><graphics><ShadowQuality value=\"invalido\"/></graphics></Settings>")]
    [InlineData("<Settings><graphics><ShadowQuality/></graphics></Settings>")]
    public async Task GtaVGraphics_RejectsKnownButIncompatibleNodes(string original)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var gtaVRoot = temporaryDirectory.Combine("Grand Theft Auto V");
        var settingsPath = temporaryDirectory.Combine("GTA V", "settings.xml");
        Directory.CreateDirectory(gtaVRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, original);
        var action = new LegacyGraphicsPresetAction(
            settingsPath,
            gtaVRoot,
            OptimizationProfile.Aggressive,
            GraphicsSettingsTarget.GtaV,
            new FakeProcessInspector(),
            new FakeGtaVProcessInspector());

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            action.ApplyAsync(Context(), CancellationToken.None));

        Assert.Contains("incompatíveis", exception.Message, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllText(settingsPath));
    }

    [Fact]
    public async Task GtaVGraphicsRollback_RechecksProcessBeforeRestoringMissingFile()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var gtaVRoot = temporaryDirectory.Combine("Grand Theft Auto V");
        var settingsPath = temporaryDirectory.Combine("GTA V", "settings.xml");
        Directory.CreateDirectory(gtaVRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(
            settingsPath,
            "<Settings><graphics><ShadowQuality value=\"3\"/></graphics></Settings>");
        var inspector = new SequencedGtaVProcessInspector(false, false, false, true);
        var action = new LegacyGraphicsPresetAction(
            settingsPath,
            gtaVRoot,
            OptimizationProfile.Aggressive,
            GraphicsSettingsTarget.GtaV,
            new FakeProcessInspector(),
            inspector);
        var context = Context();
        var result = await action.ApplyAsync(context, CancellationToken.None);
        File.Delete(settingsPath);

        await Assert.ThrowsAsync<IOException>(() =>
            action.RollbackAsync(context, result.SnapshotJson, CancellationToken.None));

        Assert.False(File.Exists(settingsPath));
        Assert.Equal(4, inspector.CallCount);
    }

    [Fact]
    public void Presets_AvoidResolutionDisplayAdapterAndDirectXSettings()
    {
        var forbidden = new[]
        {
            "ScreenWidth",
            "ScreenHeight",
            "Windowed",
            "RefreshRate",
            "AdapterIndex",
            "DX_Version"
        };

        foreach (var profile in Enum.GetValues<OptimizationProfile>())
        {
            var preset = LegacyGraphicsPresets.For(profile);
            Assert.Empty(preset.Keys.Intersect(forbidden, StringComparer.Ordinal));
        }

        Assert.Equal("0", LegacyGraphicsPresets.For(OptimizationProfile.Light)["MSAA"]);
        Assert.Equal("8", LegacyGraphicsPresets.For(OptimizationProfile.Balanced)["AnisotropicFiltering"]);
        Assert.Equal("4", LegacyGraphicsPresets.For(OptimizationProfile.Aggressive)["AnisotropicFiltering"]);
    }

    private static string? Value(XDocument document, string elementName)
    {
        return document.Descendants(elementName).Single().Attribute("value")?.Value;
    }

    private static WindowsActionContext Context()
    {
        return new WindowsActionContext
        {
            TransactionId = Guid.NewGuid(),
            StartedAtUtc = DateTimeOffset.UtcNow,
            IsElevated = false
        };
    }
}
