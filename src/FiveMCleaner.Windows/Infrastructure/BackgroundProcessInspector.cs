using System.Management;

namespace FiveMCleaner.Windows.Infrastructure;

public sealed record BackgroundProcessUsage(string ProcessName, double CpuPercent);

public interface IBackgroundProcessInspector
{
    /// <summary>
    /// Returns the process consuming the most CPU among running processes,
    /// excluding the names in <paramref name="excludedProcessNames"/> (the
    /// game/launcher itself and this app), or null when nothing relevant is
    /// found or the read failed.
    /// </summary>
    BackgroundProcessUsage? GetTopConsumer(IReadOnlyCollection<string> excludedProcessNames);
}

/// <summary>
/// Reads per-process CPU usage from WMI Win32_PerfFormattedData_PerfProc_Process
/// — a standard, driver-free performance counter class already used to power
/// Task Manager and Resource Monitor. Used only to flag that some other
/// process (not FiveM/GTA) is consuming significant CPU right now; never
/// inspects a process's memory, window content or command line.
/// </summary>
public sealed class WindowsBackgroundProcessInspector : IBackgroundProcessInspector
{
    public BackgroundProcessUsage? GetTopConsumer(IReadOnlyCollection<string> excludedProcessNames)
    {
        var excluded = new HashSet<string>(excludedProcessNames, StringComparer.OrdinalIgnoreCase)
        {
            "_Total",
            "Idle",
            "System",
            "Memory Compression"
        };

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, PercentProcessorTime FROM Win32_PerfFormattedData_PerfProc_Process");
            using var results = searcher.Get();

            string? topName = null;
            double topCpu = 0;
            foreach (ManagementObject entry in results.Cast<ManagementObject>())
            {
                using (entry)
                {
                    var name = (entry["Name"] as string)?.Trim();
                    var cpu = entry["PercentProcessorTime"] as ulong?;
                    if (string.IsNullOrWhiteSpace(name) || cpu is null || excluded.Contains(name))
                    {
                        continue;
                    }

                    var normalized = name.Contains('#', StringComparison.Ordinal)
                        ? name[..name.IndexOf('#', StringComparison.Ordinal)]
                        : name;
                    if (excluded.Contains(normalized))
                    {
                        continue;
                    }

                    if (cpu.Value > topCpu)
                    {
                        topCpu = cpu.Value;
                        topName = normalized;
                    }
                }
            }

            return topName is null ? null : new BackgroundProcessUsage(topName, topCpu);
        }
        catch (ManagementException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
