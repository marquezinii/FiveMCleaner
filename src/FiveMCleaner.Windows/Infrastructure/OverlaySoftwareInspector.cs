using System.Diagnostics;

namespace FiveMCleaner.Windows.Infrastructure;

public interface IOverlaySoftwareInspector
{
    /// <summary>Display names of known overlay/background-capture software currently running.</summary>
    IReadOnlyList<string> DetectRunningOverlayNames();
}

/// <summary>
/// Read-only, heuristic detector for well-known third-party overlay and
/// background-capture processes that can occasionally interact with a
/// full-screen game session. It only inspects process names already exposed
/// by the OS; it never reads window handles, injects code or closes anything.
/// </summary>
public sealed class WindowsOverlaySoftwareInspector : IOverlaySoftwareInspector
{
    private static readonly IReadOnlyDictionary<string, string> KnownOverlayProcessNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["nvcontainer"] = "Overlay NVIDIA (GeForce Experience)",
            ["NVIDIA Share"] = "NVIDIA Share / ShadowPlay",
            ["RTSS"] = "RivaTuner Statistics Server",
            ["Discord"] = "Overlay do Discord",
            ["GameBar"] = "Xbox Game Bar",
            ["GameBarFTServer"] = "Xbox Game Bar (captura em segundo plano)"
        };

    public IReadOnlyList<string> DetectRunningOverlayNames()
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return [];
        }

        foreach (var process in processes)
        {
            using (process)
            {
                string processName;
                try
                {
                    processName = process.ProcessName;
                }
                catch (Exception exception) when (exception is InvalidOperationException
                    or System.ComponentModel.Win32Exception
                    or NotSupportedException)
                {
                    continue;
                }

                if (KnownOverlayProcessNames.TryGetValue(processName, out var displayName))
                {
                    found.Add(displayName);
                }
            }
        }

        return found.ToArray();
    }
}
