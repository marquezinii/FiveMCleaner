using FiveMCleaner.App.Services;

namespace FiveMCleaner.Tests.App;

/// <summary>
/// Test double for <see cref="ILiveAlertService"/> letting tests control
/// exactly what <see cref="MainViewModel.CheckLiveAlertAsync"/> observes,
/// without any real network access.
/// </summary>
internal sealed class FakeLiveAlertService : ILiveAlertService
{
    private readonly LiveAlertSnapshot? snapshotToReturn;
    private readonly Exception? exceptionToThrow;

    public FakeLiveAlertService(LiveAlertSnapshot? snapshotToReturn = null, Exception? exceptionToThrow = null)
    {
        this.snapshotToReturn = snapshotToReturn;
        this.exceptionToThrow = exceptionToThrow;
    }

    public int CallCount { get; private set; }

    public Task<LiveAlertSnapshot?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        CallCount++;
        return exceptionToThrow is not null
            ? Task.FromException<LiveAlertSnapshot?>(exceptionToThrow)
            : Task.FromResult(snapshotToReturn);
    }
}
