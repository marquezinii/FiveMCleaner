using System.Xml.Linq;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Windows.Actions;
using FiveMCleaner.Windows.Infrastructure;
using Xunit;

namespace FiveMCleaner.Tests.Windows;

public sealed class QualityGraphicsPresetTests
{
    [Fact]
    public async Task QualityPreset_OnlyRaisesAllowlistedValuesAndRollsBackExactly()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var fiveMRoot = temporaryDirectory.Combine("FiveM");
        var settingsPath = temporaryDirectory.Combine("Roaming", "CitizenFX", "gta5_settings.xml");
        Directory.CreateDirectory(fiveMRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        const string original =
            "<Settings><graphics>"
            + "<ShadowQuality value=\"0\"/>"
            + "<TextureQuality value=\"3\"/>"
            + "<FXAA value=\"false\"/>"
            + "<Windowed value=\"0\"/>"
            + "</graphics></Settings>";
        File.WriteAllText(settingsPath, original);
        var fiveMInspector = new FakeProcessInspector();
        var gtaVInspector = new FakeGtaVProcessInspector();
        var action = new LegacyGraphicsPresetAction(
            settingsPath,
            fiveMRoot,
            GraphicsSettingsTarget.FiveM,
            fiveMInspector,
            gtaVInspector,
            OptimizationActionIds.ApplyQualityLegacyGraphics,
            LegacyGraphicsPresets.Quality,
            GraphicsPresetDirection.RaiseOnly);
        var context = Context();

        var result = await action.ApplyAsync(context, CancellationToken.None);

        Assert.True(result.Changed);
        var document = XDocument.Load(settingsPath);
        Assert.Equal("2", Value(document, "ShadowQuality"));
        // TextureQuality is already above the Quality preset's target (2), so it must not be lowered.
        Assert.Equal("3", Value(document, "TextureQuality"));
        Assert.Equal("true", Value(document, "FXAA"));
        // Windowed is not in the Quality preset's allowlisted keys; must remain untouched.
        Assert.Equal("0", Value(document, "Windowed"));

        await action.RollbackAsync(context, result.SnapshotJson, CancellationToken.None);

        Assert.Equal(original, File.ReadAllText(settingsPath));
    }

    [Fact]
    public void QualityPreset_NeverIncludesMsaaOrExtendedDistanceSettings()
    {
        var forbidden = new[] { "MSAA", "MSAAFragments", "MSAAQuality", "ReflectionMSAA", "TXAA_Enabled",
            "ExtendedDistanceScaling", "ExtendedShadowDistance", "MotionBlurStrength", "DoF" };

        Assert.Empty(LegacyGraphicsPresets.Quality.Keys.Intersect(forbidden, StringComparer.Ordinal));
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

public sealed class DisplayPreferencesActionTests
{
    [Theory]
    [InlineData("0", "1", "1", "0")]
    [InlineData("false", "true", "true", "false")]
    public async Task ApplyAsync_PreservesOriginalBooleanFormatAndRollsBackExactly(
        string originalWindowed,
        string originalVSync,
        string expectedWindowedAfter,
        string expectedVSyncAfter)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var fiveMRoot = temporaryDirectory.Combine("FiveM");
        var settingsPath = temporaryDirectory.Combine("Roaming", "CitizenFX", "gta5_settings.xml");
        Directory.CreateDirectory(fiveMRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var original =
            "<Settings><graphics>"
            + $"<Windowed value=\"{originalWindowed}\"/>"
            + $"<VSync value=\"{originalVSync}\"/>"
            + "</graphics></Settings>";
        File.WriteAllText(settingsPath, original);
        var fiveMInspector = new FakeProcessInspector();
        var gtaVInspector = new FakeGtaVProcessInspector();
        var action = new DisplayPreferencesAction(
            settingsPath,
            fiveMRoot,
            GraphicsSettingsTarget.FiveM,
            preferWindowedMode: true,
            enableVSync: false,
            fiveMInspector,
            gtaVInspector);
        var context = Context();

        var result = await action.ApplyAsync(context, CancellationToken.None);

        Assert.True(result.Changed);
        var document = XDocument.Load(settingsPath);
        Assert.Equal(expectedWindowedAfter, Value(document, "Windowed"));
        Assert.Equal(expectedVSyncAfter, Value(document, "VSync"));

        await action.RollbackAsync(context, result.SnapshotJson, CancellationToken.None);

        Assert.Equal(original, File.ReadAllText(settingsPath));
    }

    [Fact]
    public async Task ApplyAsync_NoChangeWhenAlreadyAtDesiredPreference()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var fiveMRoot = temporaryDirectory.Combine("FiveM");
        var settingsPath = temporaryDirectory.Combine("Roaming", "CitizenFX", "gta5_settings.xml");
        Directory.CreateDirectory(fiveMRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        const string original = "<Settings><graphics><Windowed value=\"true\"/><VSync value=\"false\"/></graphics></Settings>";
        File.WriteAllText(settingsPath, original);
        var action = new DisplayPreferencesAction(
            settingsPath,
            fiveMRoot,
            GraphicsSettingsTarget.FiveM,
            preferWindowedMode: true,
            enableVSync: false,
            new FakeProcessInspector(),
            new FakeGtaVProcessInspector());

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Equal(original, File.ReadAllText(settingsPath));
    }

    [Fact]
    public async Task ApplyAsync_RefusesToWriteWhileFiveMIsRunning()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var fiveMRoot = temporaryDirectory.Combine("FiveM");
        var settingsPath = temporaryDirectory.Combine("Roaming", "CitizenFX", "gta5_settings.xml");
        Directory.CreateDirectory(fiveMRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        const string original = "<Settings><graphics><Windowed value=\"false\"/></graphics></Settings>";
        File.WriteAllText(settingsPath, original);
        var action = new DisplayPreferencesAction(
            settingsPath,
            fiveMRoot,
            GraphicsSettingsTarget.FiveM,
            preferWindowedMode: true,
            enableVSync: true,
            new FakeProcessInspector(running: true),
            new FakeGtaVProcessInspector());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            action.ApplyAsync(Context(), CancellationToken.None));
        Assert.Equal(original, File.ReadAllText(settingsPath));
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

public sealed class GraphicsPresetRecommendationTests
{
    [Fact]
    public void Recommend_SuggestsFpsForWeakHardware()
    {
        var gpus = new[] { new GpuAdapterDetails("Intel(R) UHD Graphics", 1L * 1024 * 1024 * 1024, GpuKindGuess.LikelyIntegrated) };
        var cpu = new CpuSnapshot(2, 4, 3000, 3500);
        var ram = new RamDetailsSnapshot([new RamModuleInfo(8L * 1024 * 1024 * 1024, 2666, 2666, "DIMM0")]);

        var message = GraphicsPresetRecommendationAction.Recommend(gpus, cpu, ram, display: null);

        Assert.Contains("preset FPS", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Recommend_SuggestsQualityForStrongHardwareOnLowRefreshMonitor()
    {
        var gpus = new[] { new GpuAdapterDetails("NVIDIA GeForce RTX 4070", 12L * 1024 * 1024 * 1024, GpuKindGuess.LikelyDiscrete) };
        var cpu = new CpuSnapshot(8, 16, 4200, 5000);
        var ram = new RamDetailsSnapshot([new RamModuleInfo(32L * 1024 * 1024 * 1024, 3200, 3200, "DIMM0")]);
        var display = new DisplayConfigurationSnapshot(1920, 1080, 60, 60, HardwareGpuSchedulingState.NotSupportedOrUnknown);

        var message = GraphicsPresetRecommendationAction.Recommend(gpus, cpu, ram, display);

        Assert.Contains("preset Qualidade", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Recommend_SuggestsBalancedForMidRangeHardware()
    {
        var gpus = new[] { new GpuAdapterDetails("NVIDIA GeForce GTX 1660", 6L * 1024 * 1024 * 1024, GpuKindGuess.LikelyDiscrete) };
        var cpu = new CpuSnapshot(6, 12, 3600, 4200);
        var ram = new RamDetailsSnapshot([new RamModuleInfo(16L * 1024 * 1024 * 1024, 3000, 3000, "DIMM0")]);

        var message = GraphicsPresetRecommendationAction.Recommend(gpus, cpu, ram, display: null);

        Assert.Contains("preset Equilibrado", message, StringComparison.Ordinal);
    }
}

public sealed class TextureVramFitDiagnosisActionTests
{
    [Fact]
    public async Task ApplyAsync_WarnsWhenTextureQualityExceedsVram()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var settingsPath = temporaryDirectory.Combine("Roaming", "CitizenFX", "gta5_settings.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, "<Settings><graphics><TextureQuality value=\"3\"/></graphics></Settings>");
        var gpuInspector = new FakeGpuDetailsInspector
        {
            Snapshot = [new GpuAdapterDetails("NVIDIA GeForce GTX 1650", 4L * 1024 * 1024 * 1024, GpuKindGuess.LikelyDiscrete)]
        };
        var action = new TextureVramFitDiagnosisAction(settingsPath, gpuInspector);

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Contains("acima dos", result.Messages.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_ReportsCompatibleWhenVramIsEnough()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var settingsPath = temporaryDirectory.Combine("Roaming", "CitizenFX", "gta5_settings.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, "<Settings><graphics><TextureQuality value=\"1\"/></graphics></Settings>");
        var gpuInspector = new FakeGpuDetailsInspector
        {
            Snapshot = [new GpuAdapterDetails("NVIDIA GeForce RTX 4070", 12L * 1024 * 1024 * 1024, GpuKindGuess.LikelyDiscrete)]
        };
        var action = new TextureVramFitDiagnosisAction(settingsPath, gpuInspector);

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Contains("compatível", result.Messages.Single(), StringComparison.Ordinal);
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
