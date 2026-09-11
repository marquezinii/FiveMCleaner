using Ralven.App.Services;
using Ralven.Windows.Infrastructure;
using Xunit;

namespace Ralven.Tests.App;

public sealed class FiveMInstallationSelectionTests
{
    [Fact]
    public async Task Cache_InvalidatesWhenTheCachedExecutableChanges()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var installationRoot = temporaryDirectory.Combine("FiveM");
        Directory.CreateDirectory(Path.Combine(installationRoot, "FiveM.app", "data"));
        var executable = Path.Combine(installationRoot, "FiveM.exe");
        File.WriteAllText(executable, "v1");
        Assert.True(FiveMInstallationLocator.TryValidateLegacyCandidate(
            installationRoot,
            FiveMInstallationSource.DefaultLocation,
            out var installation));
        var cachePath = temporaryDirectory.Combine("fivem-installation.json");

        await FiveMInstallationCache.WriteAsync(cachePath, installation, TestContext.Current.CancellationToken);
        Assert.Equal(installationRoot, await FiveMInstallationCache.ReadValidRootAsync(cachePath, TestContext.Current.CancellationToken), ignoreCase: true);

        File.WriteAllText(executable, "v2-updated");

        Assert.Null(await FiveMInstallationCache.ReadValidRootAsync(cachePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetManualFiveMInstallationAsync_PersistsOnlyValidatedRootAndCanReturnToAutomatic()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var installationRoot = temporaryDirectory.Combine("Games", "FiveM");
        Directory.CreateDirectory(Path.Combine(installationRoot, "FiveM.app", "data"));
        File.WriteAllText(Path.Combine(installationRoot, "FiveM.exe"), string.Empty);
        var service = new AppOptimizationService(temporaryDirectory.Path);

        var selected = await service.SetManualFiveMInstallationAsync(installationRoot);

        Assert.True(selected.Succeeded);
        Assert.Equal(Path.GetFullPath(installationRoot), selected.Root, ignoreCase: true);
        Assert.Equal(selected.Root, (await service.LoadSettingsAsync()).ManualFiveMInstallationRoot, ignoreCase: true);

        var automatic = await service.SetManualFiveMInstallationAsync(null);

        Assert.True(automatic.Succeeded);
        Assert.Null((await service.LoadSettingsAsync()).ManualFiveMInstallationRoot);
    }

    [Fact]
    public async Task SetManualFiveMInstallationAsync_RejectsFolderNamedFiveMWithoutExecutable()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var invalidRoot = temporaryDirectory.Combine("FiveM");
        Directory.CreateDirectory(Path.Combine(invalidRoot, "FiveM.app", "data"));
        var service = new AppOptimizationService(temporaryDirectory.Path);

        var result = await service.SetManualFiveMInstallationAsync(invalidRoot);

        Assert.False(result.Succeeded);
        Assert.False(service.SettingsFileExists());
    }
}
