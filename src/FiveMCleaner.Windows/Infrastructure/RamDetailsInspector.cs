using System.Management;

namespace FiveMCleaner.Windows.Infrastructure;

public sealed record RamModuleInfo(
    long CapacityBytes,
    uint ConfiguredClockMhz,
    uint RatedClockMhz);

public sealed record RamDetailsSnapshot(IReadOnlyList<RamModuleInfo> Modules);

public interface IRamDetailsInspector
{
    RamDetailsSnapshot GetSnapshot();
}

/// <summary>
/// Reads per-module RAM details from WMI Win32_PhysicalMemory: capacity, the
/// configured (running) clock speed and the module's own rated (SPD) speed.
/// Used to build honest heuristics for single-channel and XMP/EXPO status —
/// neither is directly exposed by Windows without vendor tooling, so both are
/// presented as inferences, not facts.
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
                "SELECT Capacity, ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory");
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
                            module["ConfiguredClockSpeed"] as uint? ?? 0,
                            module["Speed"] as uint? ?? 0));
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
