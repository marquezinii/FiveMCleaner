using System.Diagnostics;

namespace FiveMCleaner.Windows.Infrastructure;

public sealed record StuckFiveMProcessSnapshot(
    bool Found,
    int ProcessId,
    string ProcessName);

public interface IStuckFiveMProcessInspector
{
    StuckFiveMProcessSnapshot GetSnapshot(string installationRoot);

    bool TryTerminate(int processId);
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
                if (!BelongsToInstallation(process, normalizedRoot))
                {
                    continue;
                }

                if (!IsNotResponding(process))
                {
                    continue;
                }

                var name = TryGetProcessName(process);
                return new StuckFiveMProcessSnapshot(true, process.Id, name);
            }
        }

        return new StuckFiveMProcessSnapshot(false, 0, string.Empty);
    }

    public bool TryTerminate(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
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

    private static bool BelongsToInstallation(Process process, string normalizedRoot)
    {
        var name = TryGetProcessName(process);
        if (WindowsFiveMProcessInspector.LooksLikeFiveMProcessName(name))
        {
            return true;
        }

        try
        {
            var fileName = process.MainModule?.FileName;
            if (fileName is null)
            {
                return false;
            }

            var normalizedProcessPath = Path.GetFullPath(fileName);
            return normalizedProcessPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                || normalizedProcessPath.StartsWith(
                    normalizedRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.ComponentModel.Win32Exception
            or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsNotResponding(Process process)
    {
        try
        {
            return !process.Responding;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.ComponentModel.Win32Exception
            or NotSupportedException)
        {
            // A process without a message loop (or one that already exited)
            // is never treated as a confirmed-stuck target.
            return false;
        }
    }

    private static string TryGetProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.ComponentModel.Win32Exception
            or NotSupportedException)
        {
            return string.Empty;
        }
    }
}
