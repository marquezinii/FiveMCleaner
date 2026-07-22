using System.Diagnostics;

namespace FiveMCleaner.Windows.Infrastructure;

public interface IGtaVProcessInspector
{
    bool IsRunningFrom(string? installationRoot);
}

public sealed class WindowsGtaVProcessInspector : IGtaVProcessInspector
{
    public bool IsRunningFrom(string? installationRoot)
    {
        var normalizedRoot = string.IsNullOrWhiteSpace(installationRoot)
            ? null
            : SafePath.Normalize(installationRoot);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (LooksLikeGtaVProcessName(process.ProcessName))
                    {
                        return true;
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException
                    or System.ComponentModel.Win32Exception
                    or NotSupportedException)
                {
                }

                if (normalizedRoot is null)
                {
                    continue;
                }

                try
                {
                    var fileName = process.MainModule?.FileName;
                    if (fileName is null)
                    {
                        continue;
                    }

                    var processPath = Path.GetFullPath(fileName);
                    if (processPath.StartsWith(
                            normalizedRoot + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException
                    or System.ComponentModel.Win32Exception
                    or NotSupportedException)
                {
                }
            }
        }

        return false;
    }

    public static bool LooksLikeGtaVProcessName(string processName)
    {
        return processName.Equals("GTA5", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("GTA5_BE", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("PlayGTAV", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("GTAVLauncher", StringComparison.OrdinalIgnoreCase);
    }
}
