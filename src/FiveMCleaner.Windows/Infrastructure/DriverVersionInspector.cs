using System.Management;

namespace FiveMCleaner.Windows.Infrastructure;

public sealed record DriverVersionInfo(string DeviceName, string DriverVersion);

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
    public DriverVersionSnapshot GetSnapshot()
    {
        var video = new List<DriverVersionInfo>();
        var network = new List<DriverVersionInfo>();
        var audio = new List<DriverVersionInfo>();
        var chipset = new List<DriverVersionInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceName, DriverVersion, DeviceClass FROM Win32_PnPSignedDriver "
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

                    var info = new DriverVersionInfo(name, version);
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
        catch (ManagementException)
        {
            return new DriverVersionSnapshot([], [], [], []);
        }
        catch (UnauthorizedAccessException)
        {
            return new DriverVersionSnapshot([], [], [], []);
        }

        return new DriverVersionSnapshot(video, network, audio, chipset);
    }
}
