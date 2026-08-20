using System.ComponentModel;
using System.Diagnostics;

namespace FiveMCleaner.Windows.Infrastructure;

/// <summary>
/// Best-effort process termination shared by the command runner and the
/// GTA V benchmark runner: never throws, since callers use it only to clean
/// up a process they are abandoning after a timeout or cancellation.
/// </summary>
internal static class ProcessTermination
{
    public static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
        catch (NotSupportedException)
        {
        }
    }
}
