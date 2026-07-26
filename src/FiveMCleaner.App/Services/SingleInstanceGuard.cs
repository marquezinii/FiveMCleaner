using System.Threading;

namespace FiveMCleaner.App.Services;

/// <summary>
/// Prevents more than one FiveMCleaner process from running at the same
/// time for a given <see cref="AppRuntimeEnvironment"/>, using a named,
/// system-wide <see cref="Mutex"/>. Scoped per environment (not globally) on
/// purpose: a developer running the Development build side by side with an
/// installed Production copy to compare behavior is a legitimate, existing
/// workflow (see <c>scripts/Start-DevelopmentApp.ps1</c>) — this only stops
/// two copies of the *same* environment from accumulating processes and tray
/// icons, which is not intentional in any known workflow.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexNamePrefix = "Local\\FiveMCleaner.SingleInstance.";

    private readonly Mutex mutex;
    private bool ownsMutex;
    private bool disposed;

    public SingleInstanceGuard(AppRuntimeEnvironment environment)
    {
        mutex = new Mutex(initiallyOwned: false, name: BuildMutexName(environment));
    }

    /// <summary>
    /// Builds the Mutex name for a given environment. Internal (not private)
    /// only so it can be asserted directly in tests without needing to
    /// actually acquire a system-wide Mutex.
    /// </summary>
    internal static string BuildMutexName(AppRuntimeEnvironment environment) =>
        $"{MutexNamePrefix}{environment}";

    /// <summary>
    /// Attempts to become the sole running instance for this environment.
    /// Returns <see langword="true"/> when no other instance currently holds
    /// the lock (this process may proceed normally), or
    /// <see langword="false"/> when another instance already owns it (the
    /// caller should not create a window and should shut down instead).
    /// </summary>
    public bool TryAcquire()
    {
        try
        {
            // A short timeout (rather than TryEnterMutex's default of an
            // immediate check) tolerates the brief window where a previous
            // instance's process is still tearing down its Mutex handle.
            ownsMutex = mutex.WaitOne(TimeSpan.FromMilliseconds(200));
        }
        catch (AbandonedMutexException)
        {
            // The previous owner crashed without releasing it; the Mutex is
            // still valid and this process now legitimately owns it.
            ownsMutex = true;
        }

        return ownsMutex;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (ownsMutex)
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        mutex.Dispose();
    }
}
