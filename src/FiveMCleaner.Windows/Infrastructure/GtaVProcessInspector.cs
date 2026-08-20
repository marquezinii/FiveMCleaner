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
                if (LooksLikeGtaVProcessName(ProcessInspection.GetNameOrEmpty(process)))
                {
                    return true;
                }

                if (normalizedRoot is null)
                {
                    continue;
                }

                if (ProcessInspection.IsExecutableWithinRoot(process, normalizedRoot))
                {
                    return true;
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
