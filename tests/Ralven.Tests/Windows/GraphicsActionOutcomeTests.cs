using Ralven.Contracts;
using Ralven.Windows.Actions;
using Ralven.Windows.Infrastructure;
using Xunit;

namespace Ralven.Tests.Windows;

public sealed class GraphicsActionOutcomeTests
{
    [Fact]
    public async Task DisplayPreferences_MissingFileIsSkipped()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var action = new DisplayPreferencesAction(
            temporaryDirectory.Combine("gta5_settings.xml"),
            temporaryDirectory.Combine("FiveM"),
            GraphicsSettingsTarget.FiveM,
            preferWindowedMode: true,
            enableVSync: false,
            new FakeProcessInspector(),
            new FakeGtaVProcessInspector());

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.Equal(ActionExecutionOutcome.Skipped, result.Outcome);
    }

    [Fact]
    public async Task DisplayPreferences_VerifiedDesiredValuesRemainNoChange()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var settingsPath = temporaryDirectory.Combine("gta5_settings.xml");
        File.WriteAllText(
            settingsPath,
            "<Settings><graphics><Windowed value=\"true\"/><VSync value=\"false\"/></graphics></Settings>");
        var action = new DisplayPreferencesAction(
            settingsPath,
            temporaryDirectory.Combine("FiveM"),
            GraphicsSettingsTarget.FiveM,
            preferWindowedMode: true,
            enableVSync: false,
            new FakeProcessInspector(),
            new FakeGtaVProcessInspector());

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.Equal(ActionExecutionOutcome.Verified, result.Outcome);
    }

    [Fact]
    public async Task DisplayPreferences_UnreadableFileFailsInsteadOfSkipping()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var settingsPath = temporaryDirectory.Combine("gta5_settings.xml");
        File.WriteAllText(
            settingsPath,
            "<Settings><graphics><Windowed value=\"false\"/><VSync value=\"true\"/></graphics></Settings>");
        var action = new DisplayPreferencesAction(
            settingsPath,
            temporaryDirectory.Combine("FiveM"),
            GraphicsSettingsTarget.FiveM,
            preferWindowedMode: true,
            enableVSync: false,
            new FakeProcessInspector(),
            new FakeGtaVProcessInspector());
        using var fileLock = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.None);

        await Assert.ThrowsAsync<IOException>(() =>
            action.ApplyAsync(Context(), CancellationToken.None));
    }

    [Fact]
    public async Task DisplayPreferences_IncompleteDataIsSkippedWithoutPartialWrite()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var settingsPath = temporaryDirectory.Combine("gta5_settings.xml");
        const string original = "<Settings><graphics><Windowed value=\"false\"/></graphics></Settings>";
        File.WriteAllText(settingsPath, original);
        var action = new DisplayPreferencesAction(
            settingsPath,
            temporaryDirectory.Combine("FiveM"),
            GraphicsSettingsTarget.FiveM,
            preferWindowedMode: true,
            enableVSync: false,
            new FakeProcessInspector(),
            new FakeGtaVProcessInspector());

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.Equal(ActionExecutionOutcome.Skipped, result.Outcome);
        Assert.Equal(original, File.ReadAllText(settingsPath));
    }

    [Fact]
    public async Task GraphicsPreset_NoCompatibleSettingIsSkipped()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var settingsPath = temporaryDirectory.Combine("gta5_settings.xml");
        File.WriteAllText(settingsPath, "<Settings><graphics><Unknown value=\"1\"/></graphics></Settings>");
        var action = new LegacyGraphicsPresetAction(
            settingsPath,
            temporaryDirectory.Combine("FiveM"),
            OptimizationProfile.Balanced,
            new FakeProcessInspector());

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.Equal(ActionExecutionOutcome.Skipped, result.Outcome);
    }

    [Fact]
    public async Task GraphicsPreset_UnreadableFileFailsInsteadOfSkipping()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var settingsPath = temporaryDirectory.Combine("gta5_settings.xml");
        File.WriteAllText(settingsPath, "<Settings><graphics><MSAA value=\"4\"/></graphics></Settings>");
        var action = new LegacyGraphicsPresetAction(
            settingsPath,
            temporaryDirectory.Combine("FiveM"),
            OptimizationProfile.Balanced,
            new FakeProcessInspector());
        using var fileLock = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.None);

        await Assert.ThrowsAsync<IOException>(() =>
            action.ApplyAsync(Context(), CancellationToken.None));
    }

    [Fact]
    public async Task GraphicsRecommendation_InsufficientHardwareIsSkipped()
    {
        var action = new GraphicsPresetRecommendationAction(
            new FakeGpuDetailsInspector(),
            new FakeCpuInspector { Snapshot = null },
            new FakeRamDetailsInspector(),
            new FakeDisplayConfigurationInspector());

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.Equal(ActionExecutionOutcome.Skipped, result.Outcome);
    }

    [Fact]
    public async Task GraphicsRecommendation_CompleteHardwareIsVerified()
    {
        var action = new GraphicsPresetRecommendationAction(
            new FakeGpuDetailsInspector
            {
                Snapshot = [new GpuAdapterDetails("GPU", 8L * 1024 * 1024 * 1024, GpuKindGuess.LikelyDiscrete)]
            },
            new FakeCpuInspector(),
            new FakeRamDetailsInspector
            {
                Snapshot = new RamDetailsSnapshot(
                    [new RamModuleInfo(16L * 1024 * 1024 * 1024, 3200)])
            },
            new FakeDisplayConfigurationInspector());

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.Equal(ActionExecutionOutcome.Verified, result.Outcome);
    }

    [Fact]
    public async Task TextureVramDiagnosis_MissingInputsAreSkipped()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var action = new TextureVramFitDiagnosisAction(
            temporaryDirectory.Combine("gta5_settings.xml"),
            new FakeGpuDetailsInspector());

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.Equal(ActionExecutionOutcome.Skipped, result.Outcome);
    }

    [Fact]
    public async Task TextureVramDiagnosis_InvalidXmlIsSkipped()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var settingsPath = temporaryDirectory.Combine("gta5_settings.xml");
        File.WriteAllText(settingsPath, "<Settings>");
        var action = new TextureVramFitDiagnosisAction(
            settingsPath,
            new FakeGpuDetailsInspector());

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.Equal(ActionExecutionOutcome.Skipped, result.Outcome);
    }

    private static WindowsActionContext Context() => new()
    {
        TransactionId = Guid.NewGuid(),
        StartedAtUtc = DateTimeOffset.UtcNow,
        IsElevated = false
    };
}
