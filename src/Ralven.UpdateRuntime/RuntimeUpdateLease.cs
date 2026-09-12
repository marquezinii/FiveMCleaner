using System.Security.Cryptography;
using System.Text;

namespace Ralven.UpdateRuntime;

/// <summary>Serializes launch supervision for one installed runtime across sessions.</summary>
public sealed class RuntimeUpdateLease : IDisposable
{
    private const string MutexNamePrefix = "Global\\Ralven.UpdateLifecycle.";
    private readonly Mutex mutex;
    private bool ownsMutex;
    private bool disposed;

    private RuntimeUpdateLease(Mutex mutex)
    {
        this.mutex = mutex;
        ownsMutex = true;
    }

    public static RuntimeUpdateLease? TryAcquire(string runtimeRoot)
    {
        var normalizedRoot = UpdatePathSafety.EnsureNoReparsePoints(runtimeRoot);
        var name = MutexNamePrefix + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRoot.ToUpperInvariant())));
        var mutex = new Mutex(initiallyOwned: false, name);
        try
        {
            if (!mutex.WaitOne(0))
            {
                mutex.Dispose();
                return null;
            }

            return new RuntimeUpdateLease(mutex);
        }
        catch (AbandonedMutexException)
        {
            return new RuntimeUpdateLease(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (ownsMutex)
        {
            ownsMutex = false;
            mutex.ReleaseMutex();
        }
        mutex.Dispose();
    }
}
