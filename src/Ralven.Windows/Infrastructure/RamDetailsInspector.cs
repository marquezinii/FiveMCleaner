using System.Management;

namespace Ralven.Windows.Infrastructure;

public sealed record RamModuleInfo(
    long CapacityBytes,
    uint ConfiguredClockMhz);

public sealed record RamDetailsSnapshot(IReadOnlyList<RamModuleInfo> Modules);

public interface IRamDetailsInspector
{
    RamDetailsSnapshot GetSnapshot();
}

/// <summary>
/// Reads per-module RAM capacity and configured clock speed from WMI
/// Win32_PhysicalMemory. Windows does not expose channel topology or active
/// XMP/EXPO state reliably through this class, so this inspector does not try
/// to infer either one.
/// </summary>
public sealed class WindowsRamDetailsInspector : IRamDetailsInspector
{
    private static readonly RamDetailsSnapshot Empty = new([]);
    private static readonly TimedSnapshotCache<RamDetailsSnapshot> Cache = new();

    public RamDetailsSnapshot GetSnapshot() => Cache.GetOrRead(Read);

    private static RamDetailsSnapshot Read()
    {
        var modules = new List<RamModuleInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Capacity, ConfiguredClockSpeed FROM Win32_PhysicalMemory");
            using var results = searcher.Get();
            foreach (ManagementObject module in results.Cast<ManagementObject>())
            {
                using (module)
                {
                    var capacity = module["Capacity"] as ulong?;
                    if (capacity is > 0)
                    {
                        modules.Add(new RamModuleInfo(
                            checked((long)capacity.Value),
                            module["ConfiguredClockSpeed"] as uint? ?? 0));
                    }
                }
            }
        }
        catch (Exception exception) when (exception is ManagementException or UnauthorizedAccessException)
        {
            return Empty;
        }

        return new RamDetailsSnapshot(modules);
    }
}
