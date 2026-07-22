using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Windows.Actions;
using FiveMCleaner.Windows.Infrastructure;
using Microsoft.Win32;
using System.Xml.Linq;
using Xunit;

namespace FiveMCleaner.Tests.Windows;

public sealed class WindowsActionHandlerTests
{
    [Fact]
    public async Task GraphicsPreset_ChangesAllowlistedNodesAndRollbackRestoresExactFile()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var installation = temporaryDirectory.Combine("FiveM");
        Directory.CreateDirectory(installation);
        var settings = temporaryDirectory.Combine("CitizenFX", "gta5_settings.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
        const string original = "<Settings><graphics><ShadowQuality value=\"3\"/><MSAA value=\"4\"/><ScreenWidth value=\"1920\"/></graphics></Settings>";
        File.WriteAllText(settings, original);
        var action = new LegacyGraphicsPresetAction(
            settings,
            installation,
            OptimizationProfile.Balanced,
            new FakeProcessInspector());
        var context = Context();

        var result = await action.ApplyAsync(context, CancellationToken.None);

        Assert.True(result.Changed);
        var updated = File.ReadAllText(settings);
        Assert.Contains("ShadowQuality value=\"1\"", updated, StringComparison.Ordinal);
        Assert.Contains("MSAA value=\"0\"", updated, StringComparison.Ordinal);
        Assert.Contains("ScreenWidth value=\"1920\"", updated, StringComparison.Ordinal);
        Assert.Equal(OptimizationActionIds.ApplyBalancedLegacyGraphics, action.Metadata.Id);

        await action.RollbackAsync(context, result.SnapshotJson, CancellationToken.None);

        Assert.Equal(original, File.ReadAllText(settings));
    }

    [Fact]
    public async Task GraphicsRollback_RefusesToOverwriteNewerUserEdit()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var installation = temporaryDirectory.Combine("FiveM");
        Directory.CreateDirectory(installation);
        var settings = temporaryDirectory.Combine("CitizenFX", "gta5_settings.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
        File.WriteAllText(settings, "<Settings><graphics><ShadowQuality value=\"3\"/></graphics></Settings>");
        var action = new LegacyGraphicsPresetAction(
            settings,
            installation,
            OptimizationProfile.Aggressive,
            new FakeProcessInspector());
        var context = Context();
        var result = await action.ApplyAsync(context, CancellationToken.None);
        File.WriteAllText(settings, "<Settings><graphics><ShadowQuality value=\"2\"/></graphics></Settings>");

        await Assert.ThrowsAsync<IOException>(() =>
            action.RollbackAsync(context, result.SnapshotJson, CancellationToken.None));
        Assert.Contains("value=\"2\"", File.ReadAllText(settings), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GraphicsPreset_OnlyChangesDirectGraphicsChildren()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var installation = temporaryDirectory.Combine("FiveM");
        var settings = temporaryDirectory.Combine("CitizenFX", "gta5_settings.xml");
        Directory.CreateDirectory(installation);
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
        File.WriteAllText(
            settings,
            "<Settings><graphics><MSAA value=\"4\"/></graphics>"
            + "<profile><MSAA value=\"4\"/></profile></Settings>");
        var action = new LegacyGraphicsPresetAction(
            settings,
            installation,
            OptimizationProfile.Light,
            new FakeProcessInspector());

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.True(result.Changed);
        var document = XDocument.Load(settings);
        Assert.Equal("0", document.Root!.Element("graphics")!.Element("MSAA")!.Attribute("value")!.Value);
        Assert.Equal("4", document.Root.Element("profile")!.Element("MSAA")!.Attribute("value")!.Value);
    }

    [Fact]
    public async Task RegistryRollback_RestoresMissingValue()
    {
        var registry = new FakeRegistryStore();
        var action = new GameModeRegistryAction(registry);
        var context = Context();

        var result = await action.ApplyAsync(context, CancellationToken.None);
        await action.RollbackAsync(context, result.SnapshotJson, CancellationToken.None);

        var address = new RegistryAddress(
            RegistryHive.CurrentUser,
            @"Software\Microsoft\GameBar",
            "AutoGameModeEnabled");
        Assert.False(registry.Read(address).Exists);
    }

    [Fact]
    public async Task RegistryRollback_PreservesValueChangedAfterOptimization()
    {
        var registry = new FakeRegistryStore();
        var address = new RegistryAddress(
            RegistryHive.CurrentUser,
            @"Software\Microsoft\GameBar",
            "AutoGameModeEnabled");
        registry.Write(address, RegistryValueState.FromDword(0));
        var action = new GameModeRegistryAction(registry);
        var context = Context();
        var result = await action.ApplyAsync(context, CancellationToken.None);
        registry.Write(address, RegistryValueState.FromDword(2));

        await Assert.ThrowsAsync<IOException>(() =>
            action.RollbackAsync(context, result.SnapshotJson, CancellationToken.None));

        Assert.Equal(2, registry.Read(address).NumericValue);
    }

    [Fact]
    public async Task RegistryRollback_RejectsSnapshotOutsideActionAllowlist()
    {
        var registry = new FakeRegistryStore();
        var action = new GameModeRegistryAction(registry);
        var context = Context();
        var maliciousAddress = new RegistryAddress(
            RegistryHive.CurrentUser,
            @"Software\FiveMCleanerTests\OutsideAllowlist",
            "UnexpectedValue");
        var tampered = WindowsActionSnapshot.Serialize(
            new RegistryMutationSnapshot(
            [
                new RegistryMutationSnapshotEntry(
                    maliciousAddress,
                    RegistryValueState.FromDword(9),
                    RegistryValueState.FromDword(1))
            ]));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            action.RollbackAsync(context, tampered, CancellationToken.None));

        Assert.False(registry.Read(maliciousAddress).Exists);
    }

    [Fact]
    public async Task RegistryRollback_RejectsEmptySnapshot()
    {
        var registry = new FakeRegistryStore();
        var action = new GameModeRegistryAction(registry);
        var emptySnapshot = WindowsActionSnapshot.Serialize(
            new RegistryMutationSnapshot([]));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            action.RollbackAsync(Context(), emptySnapshot, CancellationToken.None));
    }

    [Fact]
    public async Task GameDvrAction_DisablesOnlyHistoricalBackgroundCapture()
    {
        var registry = new FakeRegistryStore();
        var historical = new RegistryAddress(
            RegistryHive.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\GameDVR",
            "HistoricalCaptureEnabled");
        var manualCapture = new RegistryAddress(
            RegistryHive.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\GameDVR",
            "AppCaptureEnabled");
        var legacyToggle = new RegistryAddress(
            RegistryHive.CurrentUser,
            @"System\GameConfigStore",
            "GameDVR_Enabled");
        registry.Write(historical, RegistryValueState.FromDword(1));
        registry.Write(manualCapture, RegistryValueState.FromDword(1));
        registry.Write(legacyToggle, RegistryValueState.FromDword(1));

        var result = await new GameDvrRegistryAction(registry)
            .ApplyAsync(Context(), CancellationToken.None);

        Assert.True(result.Changed);
        Assert.Equal(0, registry.Read(historical).NumericValue);
        Assert.Equal(1, registry.Read(manualCapture).NumericValue);
        Assert.Equal(1, registry.Read(legacyToggle).NumericValue);
    }

    [Fact]
    public async Task GpuPreferenceAction_IncludesKnownFiveMRendererFromCanonicalCache()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var root = temporaryDirectory.Combine("FiveM");
        var rendererDirectory = Path.Combine(root, "FiveM.app", "data", "cache", "subprocess");
        Directory.CreateDirectory(rendererDirectory);
        var launcher = Path.Combine(root, "FiveM.exe");
        var renderer = Path.Combine(rendererDirectory, "FiveM_b3258_GTAProcess.exe");
        var decoy = Path.Combine(rendererDirectory, "FiveM_bbad_GTAProcess.exe");
        File.WriteAllText(launcher, string.Empty);
        File.WriteAllText(renderer, string.Empty);
        File.WriteAllText(decoy, string.Empty);
        var registry = new FakeRegistryStore();
        var subKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
        var launcherAddress = new RegistryAddress(
            RegistryHive.CurrentUser,
            subKey,
            launcher);
        registry.Write(
            launcherAddress,
            RegistryValueState.FromString("GpuPreference=1;AutoHDREnable=2097;"));
        var action = new GpuPreferenceRegistryAction(registry, launcher, root);
        var context = Context();

        var result = await action.ApplyAsync(context, CancellationToken.None);

        Assert.True(result.Changed);
        Assert.Equal(
            "GpuPreference=2;AutoHDREnable=2097;",
            registry.Read(launcherAddress).StringValue);
        Assert.Equal(
            "GpuPreference=2;",
            registry.Read(new RegistryAddress(RegistryHive.CurrentUser, subKey, renderer)).StringValue);
        Assert.False(registry.Read(
            new RegistryAddress(RegistryHive.CurrentUser, subKey, decoy)).Exists);

        File.Delete(renderer);
        await action.RollbackAsync(context, result.SnapshotJson, CancellationToken.None);
        Assert.Equal(
            "GpuPreference=1;AutoHDREnable=2097;",
            registry.Read(launcherAddress).StringValue);
        Assert.False(registry.Read(
            new RegistryAddress(RegistryHive.CurrentUser, subKey, renderer)).Exists);
    }

    [Fact]
    public async Task SessionPowerPlan_RequiresAcAndRestoresPreviousScheme()
    {
        var controller = new FakePowerPlanController();
        var previous = controller.ActiveScheme;
        var powerStatus = new FakePowerStatusProvider(isOnAcPower: false);
        var action = new SessionPerformancePowerPlanAction(controller, powerStatus);
        var context = Context(elevated: true);

        var batteryResult = await action.ApplyAsync(context, CancellationToken.None);
        Assert.False(batteryResult.Changed);
        Assert.Equal(previous, controller.ActiveScheme);

        powerStatus.OnAcPower = true;
        var result = await action.ApplyAsync(context, CancellationToken.None);
        Assert.True(result.Changed);
        Assert.Equal(controller.PerformanceScheme, controller.ActiveScheme);

        await action.RollbackAsync(context, result.SnapshotJson, CancellationToken.None);
        Assert.Equal(previous, controller.ActiveScheme);
    }

    [Fact]
    public async Task VisualEffectsRollback_PreservesNewerUserChoice()
    {
        var controller = new FakeVisualEffectsController();
        var action = new VisualEffectsAction(controller);
        var context = Context();
        var result = await action.ApplyAsync(context, CancellationToken.None);
        controller.State = new VisualEffectsState(true, false, true);

        await Assert.ThrowsAsync<IOException>(() =>
            action.RollbackAsync(context, result.SnapshotJson, CancellationToken.None));
        Assert.Equal(new VisualEffectsState(true, false, true), controller.State);
    }

    private static WindowsActionContext Context(bool elevated = false)
    {
        return new WindowsActionContext
        {
            TransactionId = Guid.NewGuid(),
            StartedAtUtc = DateTimeOffset.UtcNow,
            IsElevated = elevated
        };
    }
}
