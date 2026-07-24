using Microsoft.Win32;

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
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var video = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Video");
            if (video is null)
            {
                return new GpuVendorSnapshot([]);
            }

            foreach (var deviceKeyName in video.GetSubKeyNames())
            {
                using var device = video.OpenSubKey(deviceKeyName);
                if (device is null)
                {
                    continue;
                }

                foreach (var adapterKeyName in device.GetSubKeyNames()
                             .Where(name => name.Length == 4 && name.All(char.IsDigit)))
                {
                    using var adapter = device.OpenSubKey(adapterKeyName);
                    var name = (adapter?.GetValue("DriverDesc") as string)?.Trim();
                    if (!string.IsNullOrWhiteSpace(name)
                        && !name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase))
                    {
                        names.Add(name);
                    }
                }
            }
        }
        catch (Exception exception) when (exception is System.Security.SecurityException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            return new GpuVendorSnapshot([]);
        }

        return new GpuVendorSnapshot(names.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
