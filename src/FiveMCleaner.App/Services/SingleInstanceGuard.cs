using System.IO;
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
/// <para>
/// The winning instance can also listen for activation requests raised by a
/// losing launch (see <see cref="ListenForActivation"/>) so that opening the
/// app again brings the already-running window to the foreground instead of
/// silently doing nothing.
/// </para>
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexNamePrefix = "Local\\FiveMCleaner.SingleInstance.";
    private const string ActivationEventSuffix = ".Activate";

    private readonly Mutex mutex;
    private readonly string activationEventName;
    private EventWaitHandle? activationEvent;
    private Action? onActivationRequested;
    private Thread? activationListener;
    private bool ownsMutex;
    private bool disposed;

    public SingleInstanceGuard(AppRuntimeEnvironment environment)
        : this(BuildMutexName(environment))
    {
    }

    internal SingleInstanceGuard(string mutexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        activationEventName = mutexName + ActivationEventSuffix;
        try
        {
            mutex = new Mutex(initiallyOwned: false, name: mutexName);
        }
        catch (UnauthorizedAccessException)
        {
            mutex = null!;
            ownsMutex = false;
        }
    }

    /// <summary>
    /// Builds the Mutex name for a given environment. Internal (not private)
    /// only so it can be asserted directly in tests without needing to
    /// actually acquire a system-wide Mutex.
    /// </summary>
    internal static string BuildMutexName(AppRuntimeEnvironment environment) =>
        $"{MutexNamePrefix}{environment}";

    /// <summary>
    /// Builds the name of the auto-reset event used to ask the running
    /// instance to bring its window to the foreground.
    /// </summary>
    internal static string BuildActivationEventName(AppRuntimeEnvironment environment) =>
        $"{MutexNamePrefix}{environment}{ActivationEventSuffix}";

    /// <summary>
    /// Attempts to become the sole running instance for this environment.
    /// Returns <see langword="true"/> when no other instance currently holds
    /// the lock (this process may proceed normally), or
    /// <see langword="false"/> when another instance already owns it (the
    /// caller should not create a window and should shut down instead, after
    /// asking the owner to come to the foreground via
    /// <see cref="RequestActivation"/>).
    /// </summary>
    public bool TryAcquire()
    {
        if (mutex is null)
        {
            return false;
        }

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
        catch (UnauthorizedAccessException)
        {
            ownsMutex = false;
        }

        if (ownsMutex)
        {
            // Create the activation event immediately after winning the
            // Mutex, so a second launch that happens during this process's
            // startup (before ListenForActivation is called) still has a
            // kernel object to signal: an auto-reset event stays signaled
            // until a waiter consumes it, so no request is lost.
            EnsureActivationEvent();
        }

        return ownsMutex;
    }

    /// <summary>
    /// Asks the running instance (if any) to bring its main window to the
    /// foreground. Called by a losing instance right before it shuts down;
    /// the owner's <see cref="ListenForActivation"/> callback observes the
    /// request.
    /// </summary>
    public void RequestActivation()
    {
        if (mutex is null)
        {
            return;
        }

        try
        {
            // Creating the event with the same name opens the object already
            // owned by the running instance, or creates a fresh one if the
            // owner has not done so yet — either way the Set below is
            // observed by the owner's listener.
            using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, activationEventName);
            signal.Set();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            // The running instance has no listener or this process cannot
            // reach it; there is nothing useful a losing instance can do.
        }
    }

    /// <summary>
    /// Starts a background listener that invokes
    /// <paramref name="onActivationRequested"/> whenever another launch asks
    /// this instance to come to the foreground. The callback runs on the
    /// listener thread; the caller must marshal it to its own thread (for
    /// example, the WPF Dispatcher) as needed.
    /// </summary>
    public void ListenForActivation(Action onActivationRequested)
    {
        ArgumentNullException.ThrowIfNull(onActivationRequested);
        ObjectDisposedException.ThrowIf(disposed, this);

        EnsureActivationEvent();
        this.onActivationRequested = onActivationRequested;
        if (activationEvent is not null && activationListener is null)
        {
            activationListener = new Thread(ActivationListenerLoop)
            {
                IsBackground = true,
                Name = "FiveMCleaner.SingleInstance.ActivationListener"
            };
            activationListener.Start();
        }
    }

    private void EnsureActivationEvent()
    {
        if (activationEvent is not null)
        {
            return;
        }

        try
        {
            activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, activationEventName);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            activationEvent = null;
        }
    }

    private void ActivationListenerLoop()
    {
        while (!disposed)
        {
            var current = activationEvent;
            if (current is null)
            {
                return;
            }

            bool signaled;
            try
            {
                // Short wait so disposal is observed quickly (Dispose wakes
                // this thread by closing the handle / setting the flag).
                signaled = current.WaitOne(TimeSpan.FromMilliseconds(200));
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (disposed)
            {
                return;
            }

            if (!signaled)
            {
                continue;
            }

            onActivationRequested?.Invoke();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (ownsMutex && mutex is not null)
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch (Exception exception) when (exception is ObjectDisposedException or ApplicationException or UnauthorizedAccessException)
            {
            }
        }

        try
        {
            activationEvent?.Dispose();
        }
        catch (Exception exception) when (exception is ObjectDisposedException or UnauthorizedAccessException)
        {
        }

        activationListener = null;
        mutex?.Dispose();
    }
}
