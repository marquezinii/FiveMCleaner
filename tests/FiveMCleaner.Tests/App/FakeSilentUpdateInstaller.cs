using FiveMCleaner.App.Services;

namespace FiveMCleaner.Tests.App;

/// <summary>
/// Test double for <see cref="ISilentUpdateInstaller"/> letting tests control
/// exactly what <see cref="MainViewModel.DownloadAndInstallUpdateAsync"/> and
/// <see cref="MainViewModel.InstallDownloadedUpdateAsync"/> observe, without
/// ever starting a real process.
/// </summary>
internal sealed class FakeSilentUpdateInstaller : ISilentUpdateInstaller
{
    private readonly SilentUpdateLaunch launchToReturn;
    private readonly Exception? exceptionToThrow;

    public FakeSilentUpdateInstaller(
        SilentUpdateLaunch? launchToReturn = null,
        Exception? exceptionToThrow = null)
    {
        this.launchToReturn = launchToReturn ?? SilentUpdateLaunch.Running();
        this.exceptionToThrow = exceptionToThrow;
    }

    public int StartCallCount { get; private set; }

    public DownloadedUpdate? LastRequestedUpdate { get; private set; }

    public Task<SilentUpdateLaunch> StartAsync(
        DownloadedUpdate update,
        CancellationToken cancellationToken = default)
    {
        StartCallCount++;
        LastRequestedUpdate = update;
        return exceptionToThrow is not null
            ? Task.FromException<SilentUpdateLaunch>(exceptionToThrow)
            : Task.FromResult(launchToReturn);
    }
}
