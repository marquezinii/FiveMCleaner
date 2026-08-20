using System.Globalization;
using System.Management;

namespace FiveMCleaner.Windows.Infrastructure;

public sealed record DriverVersionInfo(string DeviceName, string DriverVersion, DateTimeOffset? DriverDate = null);

public sealed record DriverVersionSnapshot(
    IReadOnlyList<DriverVersionInfo> Video,
    IReadOnlyList<DriverVersionInfo> Network,
    IReadOnlyList<DriverVersionInfo> Audio,
    IReadOnlyList<DriverVersionInfo> Chipset);

public interface IDriverVersionInspector
{
    DriverVersionSnapshot GetSnapshot();
}

/// <summary>
/// Reads installed driver versions from WMI Win32_PnPSignedDriver, grouped
/// by device class. Chipset drivers do not have a dedicated WMI device
/// class, so they are approximated by matching "chipset" in the device name
/// among System-class entries; when nothing matches, the chipset group is
/// simply empty rather than guessing a device.
/// </summary>
public sealed class WindowsDriverVersionInspector : IDriverVersionInspector
{
    private static readonly DriverVersionSnapshot Empty = new([], [], [], []);
    private static readonly TimedSnapshotCache<DriverVersionSnapshot> Cache = new();

    public DriverVersionSnapshot GetSnapshot() => Cache.GetOrRead(Read);

    private static DriverVersionSnapshot Read()
    {
        var video = new List<DriverVersionInfo>();
        var network = new List<DriverVersionInfo>();
        var audio = new List<DriverVersionInfo>();
        var chipset = new List<DriverVersionInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceName, DriverVersion, DriverDate, DeviceClass FROM Win32_PnPSignedDriver "
                    + "WHERE DeviceClass = 'DISPLAY' OR DeviceClass = 'NET' "
                    + "OR DeviceClass = 'MEDIA' OR DeviceClass = 'SYSTEM'");
            using var results = searcher.Get();
            foreach (ManagementObject entry in results.Cast<ManagementObject>())
            {
                using (entry)
                {
                    var name = (entry["DeviceName"] as string)?.Trim();
                    var version = (entry["DriverVersion"] as string)?.Trim();
                    var deviceClass = (entry["DeviceClass"] as string)?.Trim();
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version))
                    {
                        continue;
                    }

                    var driverDate = ParseWmiDate(entry["DriverDate"] as string);
                    var info = new DriverVersionInfo(name, version, driverDate);
                    switch (deviceClass)
                    {
                        case "DISPLAY":
                            video.Add(info);
                            break;
                        case "NET":
                            network.Add(info);
                            break;
                        case "MEDIA":
                            audio.Add(info);
                            break;
                        case "SYSTEM" when name.Contains("chipset", StringComparison.OrdinalIgnoreCase):
                            chipset.Add(info);
                            break;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is ManagementException or UnauthorizedAccessException)
        {
            return Empty;
        }

        return new DriverVersionSnapshot(video, network, audio, chipset);
    }

    /// <summary>
    /// Parses WMI's DMTF datetime format (e.g. "20230115000000.000000-000")
    /// for <c>DriverDate</c>. Returns null on anything unexpected instead of
    /// throwing -- driver age is an informational extra, never load-bearing.
    /// </summary>
    private static DateTimeOffset? ParseWmiDate(string? dmtfDate)
    {
        if (string.IsNullOrWhiteSpace(dmtfDate) || dmtfDate.Length < 8)
        {
            return null;
        }

        try
        {
            var year = int.Parse(dmtfDate[..4], CultureInfo.InvariantCulture);
            var month = int.Parse(dmtfDate[4..6], CultureInfo.InvariantCulture);
            var day = int.Parse(dmtfDate[6..8], CultureInfo.InvariantCulture);
            return new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero);
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or FormatException)
        {
            return null;
        }
    }
}
