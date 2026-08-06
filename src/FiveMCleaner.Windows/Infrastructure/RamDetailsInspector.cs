using System.Management;
using System.Threading;

namespace FiveMCleaner.Windows.Infrastructure;

public sealed record RamModuleInfo(
    long CapacityBytes,
    uint ConfiguredClockMhz,
    uint RatedClockMhz,
    string? DeviceLocator);

public sealed record RamDetailsSnapshot(IReadOnlyList<RamModuleInfo> Modules);

public interface IRamDetailsInspector
{
    RamDetailsSnapshot GetSnapshot();
}

/// <summary>
/// Reads per-module RAM details from WMI Win32_PhysicalMemory: capacity,
/// configured (running) clock speed, the module's own rated (SPD) speed and
/// slot locator text. Used to build honest heuristics for single-channel and
/// XMP/EXPO status — neither is directly exposed by Windows without vendor
/// tooling, so both are presented as inferences, not facts.
///
/// Caches results for 30 seconds to avoid repeated WMI queries during a single session.
/// </summary>
public sealed class WindowsRamDetailsInspector : IRamDetailsInspector
{
    private static readonly object CacheLock = new();
    private static RamDetailsSnapshot? cachedSnapshot;
    private static DateTimeOffset? cachedAt;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public RamDetailsSnapshot GetSnapshot()
    {
        // Check cache first
        lock (CacheLock)
        {
            if (cachedSnapshot is not null && cachedAt is not null &&
                DateTimeOffset.UtcNow - cachedAt.Value < CacheTtl)
            {
                return cachedSnapshot;
            }
        }

        var snapshot = GetSnapshotInternal();

        // Update cache
        lock (CacheLock)
        {
            cachedSnapshot = snapshot;
            cachedAt = DateTimeOffset.UtcNow;
        }

        return snapshot;
    }

    private static RamDetailsSnapshot GetSnapshotInternal()
    {
        var modules = new List<RamModuleInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Capacity, ConfiguredClockSpeed, Speed, DeviceLocator FROM Win32_PhysicalMemory");
            using var results = searcher.Get();
            foreach (ManagementObject module in results.Cast<ManagementObject>())
            {
                using (module)
                {
                    var capacity = module["Capacity"] as ulong?;
                    var configured = module["ConfiguredClockSpeed"] as uint?;
                    var rated = module["Speed"] as uint?;
                    var locator = module["DeviceLocator"] as string;
                    if (capacity is > 0)
                    {
                        modules.Add(new RamModuleInfo(
                            checked((long)capacity.Value),
                            configured ?? 0,
                            rated ?? 0,
                            locator));
                    }
                }
            }
        }
        catch (ManagementException)
        {
            return new RamDetailsSnapshot([]);
        }
        catch (UnauthorizedAccessException)
        {
            return new RamDetailsSnapshot([]);
        }

        return new RamDetailsSnapshot(modules);
    }
}
