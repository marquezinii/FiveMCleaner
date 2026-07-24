using System.Management;

namespace FiveMCleaner.Windows.Infrastructure;

public sealed record CpuSnapshot(
    int PhysicalCores,
    int LogicalThreads,
    uint CurrentClockMhz,
    uint MaxClockMhz);

public interface ICpuInspector
{
    CpuSnapshot? GetSnapshot();
}

/// <summary>
/// Reads CPU inventory from WMI Win32_Processor — a standard, driver-free
/// class already present on every supported Windows edition. Returns null
/// when the read fails; callers must report that honestly instead of
/// guessing. Follows the same try/graceful-null WMI pattern already used for
/// RAM module layout in <c>AppOptimizationService</c>.
/// </summary>
public sealed class WindowsCpuInspector : ICpuInspector
{
    public CpuSnapshot? GetSnapshot()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT NumberOfCores, NumberOfLogicalProcessors, CurrentClockSpeed, MaxClockSpeed "
                    + "FROM Win32_Processor");
            using var results = searcher.Get();
            foreach (ManagementObject processor in results.Cast<ManagementObject>())
            {
                using (processor)
                {
                    var cores = processor["NumberOfCores"] as uint?;
                    var threads = processor["NumberOfLogicalProcessors"] as uint?;
                    var current = processor["CurrentClockSpeed"] as uint?;
                    var max = processor["MaxClockSpeed"] as uint?;
                    if (cores is > 0 && threads is > 0 && current is > 0 && max is > 0)
                    {
                        return new CpuSnapshot(
                            checked((int)cores.Value),
                            checked((int)threads.Value),
                            current.Value,
                            max.Value);
                    }
                }
            }

            return null;
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
