using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Windows.Actions;
using FiveMCleaner.Windows.Infrastructure;
using Microsoft.Win32;
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
        File.WriteAllText(settings, "<Settings><ShadowQuality value=\"3\"/></Settings>");
        var action = new LegacyGraphicsPresetAction(
            settings,
            installation,
            OptimizationProfile.Aggressive,
            new FakeProcessInspector());
        var context = Context();
        var result = await action.ApplyAsync(context, CancellationToken.None);
        File.WriteAllText(settings, "<Settings><ShadowQuality value=\"2\"/></Settings>");

        await Assert.ThrowsAsync<IOException>(() =>
            action.RollbackAsync(context, result.SnapshotJson, CancellationToken.None));
        Assert.Contains("value=\"2\"", File.ReadAllText(settings), StringComparison.Ordinal);
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
