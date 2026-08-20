using System.Diagnostics;

namespace FiveMCleaner.Windows.Infrastructure;

public interface IFiveMProcessInspector
{
    bool IsRunningFrom(string installationRoot);
}

public sealed class WindowsFiveMProcessInspector : IFiveMProcessInspector
{
    public bool IsRunningFrom(string installationRoot)
    {
        var normalizedRoot = SafePath.Normalize(installationRoot);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var processName = ProcessInspection.GetNameOrEmpty(process);

                // O nome continua disponível quando MainModule é negado por uma
                // diferença de elevação. Nesse caso, bloquear é a decisão segura.
                if (LooksLikeFiveMProcessName(processName))
                {
                    return true;
                }

                if (ProcessInspection.IsExecutableWithinRoot(process, normalizedRoot))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool LooksLikeFiveMProcessName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        // Process.ProcessName does not include the .exe suffix. FiveM's runtime
        // children use the FiveM_* and CitizenFX_* families. Matching a bare
        // substring would make FiveMCleaner detect itself as the game.
        return processName.Equals("FiveM", StringComparison.OrdinalIgnoreCase)
            || processName.StartsWith("FiveM_", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("CitizenFX", StringComparison.OrdinalIgnoreCase)
            || processName.StartsWith("CitizenFX_", StringComparison.OrdinalIgnoreCase);
    }
}
