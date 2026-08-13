using System.Diagnostics;

namespace FiveMCleaner.Windows.Infrastructure;

public sealed record StuckFiveMProcessSnapshot(
    bool Found,
    int ProcessId,
    string ProcessName);

public interface IStuckFiveMProcessInspector
{
    StuckFiveMProcessSnapshot GetSnapshot(string installationRoot);
}

public interface IFiveMProcessTerminator
{
    bool TryTerminate(StuckFiveMProcessSnapshot snapshot, string installationRoot);
}

/// <summary>
/// Finds a FiveM process that is demonstrably stuck (its image belongs to the
/// FiveM installation and it is not responding to the message loop) so it can
/// be terminated to unblock a cache cleanup. Never targets any other process.
/// </summary>
public sealed class WindowsStuckFiveMProcessInspector : IStuckFiveMProcessInspector
{
    public StuckFiveMProcessSnapshot GetSnapshot(string installationRoot)
    {
        var normalizedRoot = SafePath.Normalize(installationRoot);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (!ProcessInspection.IsExecutableWithinRoot(process, normalizedRoot))
                {
                    continue;
                }

                if (!ProcessInspection.IsNotResponding(process))
                {
                    continue;
                }

                var name = ProcessInspection.GetNameOrEmpty(process);
                return new StuckFiveMProcessSnapshot(true, process.Id, name);
            }
        }

        return new StuckFiveMProcessSnapshot(false, 0, string.Empty);
    }
}

/// <summary>
/// Terminates only the exact stuck FiveM process that was inspected. The
/// executable path and non-responsive state are checked again immediately
/// before termination to reject PID reuse and stale observations.
/// </summary>
public sealed class WindowsFiveMProcessTerminator : IFiveMProcessTerminator
{
    public bool TryTerminate(StuckFiveMProcessSnapshot snapshot, string installationRoot)
    {
        if (!snapshot.Found || snapshot.ProcessId <= 0)
        {
            return false;
        }

        var normalizedRoot = SafePath.Normalize(installationRoot);
        try
        {
            using var process = Process.GetProcessById(snapshot.ProcessId);
            if (!ProcessInspection.GetNameOrEmpty(process).Equals(
                    snapshot.ProcessName,
                    StringComparison.OrdinalIgnoreCase)
                || !ProcessInspection.IsExecutableWithinRoot(process, normalizedRoot)
                || !ProcessInspection.IsNotResponding(process))
            {
                return false;
            }

            process.Kill(entireProcessTree: false);
            process.WaitForExit(5000);
            return process.HasExited;
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or System.ComponentModel.Win32Exception
            or NotSupportedException)
        {
            return false;
        }
    }
}
