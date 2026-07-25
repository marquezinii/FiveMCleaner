using System.Management;

namespace FiveMCleaner.Windows.Infrastructure;

public sealed record PhysicalDiskInfo(
    string FriendlyName,
    string MediaTypeLabel,
    bool IsHealthy,
    string HealthStatusLabel);

public sealed record StorageHealthSnapshot(IReadOnlyList<PhysicalDiskInfo> Disks);

public interface IStorageHealthInspector
{
    StorageHealthSnapshot GetSnapshot();
}

/// <summary>
/// Reads physical disk type (HDD/SATA SSD/NVMe) and predictive health status
/// from MSFT_PhysicalDisk (root\Microsoft\Windows\Storage) — the modern,
/// driver-free Storage Management API namespace built into Windows 8+ that
/// covers both SATA S.M.A.R.T. and NVMe health uniformly. It reports on all
/// physical disks rather than guessing which one hosts a specific
/// installation, since mapping a drive letter to a physical disk reliably
/// would require a fragile multi-class WMI join across storage pools,
/// dynamic disks and virtual disks.
/// </summary>
public sealed class WindowsStorageHealthInspector : IStorageHealthInspector
{
    public StorageHealthSnapshot GetSnapshot()
    {
        var disks = new List<PhysicalDiskInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\Microsoft\Windows\Storage",
                "SELECT FriendlyName, MediaType, HealthStatus FROM MSFT_PhysicalDisk");
            using var results = searcher.Get();
            foreach (ManagementObject disk in results.Cast<ManagementObject>())
            {
                using (disk)
                {
                    var name = (disk["FriendlyName"] as string)?.Trim();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var mediaType = Convert.ToUInt16(disk["MediaType"] ?? (ushort)0);
                    var healthStatus = Convert.ToUInt16(disk["HealthStatus"] ?? (ushort)5);
                    disks.Add(new PhysicalDiskInfo(
                        name,
                        MediaTypeLabel(mediaType),
                        healthStatus == 0,
                        HealthLabel(healthStatus)));
                }
            }
        }
        catch (ManagementException)
        {
            return new StorageHealthSnapshot([]);
        }
        catch (UnauthorizedAccessException)
        {
            return new StorageHealthSnapshot([]);
        }

        return new StorageHealthSnapshot(disks);
    }

    // MSFT_PhysicalDisk.MediaType: 0=Unspecified, 3=HDD, 4=SSD (SSD includes NVMe;
    // Windows does not split NVMe into its own MediaType value).
    private static string MediaTypeLabel(ushort mediaType) => mediaType switch
    {
        3 => "HDD",
        4 => "SSD/NVMe",
        _ => "Desconhecido"
    };

    // MSFT_PhysicalDisk.HealthStatus: 0=Healthy, 1=Warning, 2=Unhealthy, 5=Unknown.
    private static string HealthLabel(ushort healthStatus) => healthStatus switch
    {
        0 => "Saudável",
        1 => "Aviso",
        2 => "Não saudável",
        _ => "Desconhecido"
    };
}
