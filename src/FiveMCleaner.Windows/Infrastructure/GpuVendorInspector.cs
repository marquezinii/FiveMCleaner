namespace FiveMCleaner.Windows.Infrastructure;

public sealed record GpuVendorSnapshot(IReadOnlyList<string> DriverDescriptions);

public interface IGpuVendorInspector
{
    GpuVendorSnapshot GetSnapshot();
}

/// <summary>
/// Reads GPU driver descriptions from the same registry location used by the
/// app's own hardware diagnosis (SYSTEM\CurrentControlSet\Control\Video). It
/// never writes anything and never opens NVIDIA/AMD/Intel control panels or
/// their driver profile stores: writing to those is explicitly out of scope
/// per docs/safety.md.
/// </summary>
public sealed class WindowsGpuVendorInspector : IGpuVendorInspector
{
    public GpuVendorSnapshot GetSnapshot()
    {
        var names = GpuAdapterRegistryReader.ReadAll()
            .Select(adapter => adapter.DriverDescription)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new GpuVendorSnapshot(names);
    }
}
