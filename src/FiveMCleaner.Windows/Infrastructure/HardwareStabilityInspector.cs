using System.Diagnostics.Eventing.Reader;
using System.Management;

namespace FiveMCleaner.Windows.Infrastructure;

public sealed record HardwareStabilitySnapshot(
    int RecentWheaEventCount,
    int RecentMemoryFlavoredWheaEventCount,
    DateTime? BiosReleaseDateUtc);

public interface IHardwareStabilityInspector
{
    HardwareStabilitySnapshot GetSnapshot();
}

/// <summary>
/// Reads two independent, driver-free stability signals: recent
/// Kernel-WHEA hardware error events from the Windows Event Log (the same
/// mechanism Reliability Monitor and Event Viewer use — no extra driver
/// required), and the BIOS release date from WMI Win32_BIOS. It never
/// attempts to read Resizable BAR/Above 4G Decoding/Smart Access Memory:
/// there is no public, driver-free Windows API that exposes that state
/// reliably, and guessing from PCI BAR sizes would risk reporting something
/// false with confidence, which this product avoids.
/// </summary>
public sealed class WindowsHardwareStabilityInspector : IHardwareStabilityInspector
{
    private static readonly TimeSpan LookbackWindow = TimeSpan.FromDays(30);

    public HardwareStabilitySnapshot GetSnapshot()
    {
        var (total, memoryFlavored) = ReadRecentWheaEvents();
        return new HardwareStabilitySnapshot(total, memoryFlavored, ReadBiosReleaseDate());
    }

    private static (int Total, int MemoryFlavored) ReadRecentWheaEvents()
    {
        try
        {
            var cutoff = DateTime.UtcNow - LookbackWindow;
            var query = new EventLogQuery(
                "System",
                PathType.LogName,
                "*[System[Provider[@Name='Microsoft-Windows-Kernel-WHEA']]]")
            {
                ReverseDirection = true
            };
            using var reader = new EventLogReader(query);

            var total = 0;
            var memoryFlavored = 0;
            const int maxEventsToInspect = 200;
            for (var inspected = 0; inspected < maxEventsToInspect; inspected++)
            {
                using var entry = reader.ReadEvent();
                if (entry is null)
                {
                    break;
                }

                using (entry)
                {
                    if (entry.TimeCreated is { } created && created.ToUniversalTime() < cutoff)
                    {
                        break;
                    }

                    total++;
                    var description = SafeFormatDescription(entry);
                    if (description is not null
                        && (description.Contains("memory", StringComparison.OrdinalIgnoreCase)
                            || description.Contains("memória", StringComparison.OrdinalIgnoreCase)))
                    {
                        memoryFlavored++;
                    }
                }
            }

            return (total, memoryFlavored);
        }
        catch (Exception exception) when (exception is EventLogNotFoundException
            or EventLogException
            or UnauthorizedAccessException)
        {
            return (0, 0);
        }
    }

    private static string? SafeFormatDescription(EventRecord entry)
    {
        try
        {
            return entry.FormatDescription();
        }
        catch (EventLogException)
        {
            return null;
        }
    }

    private static DateTime? ReadBiosReleaseDate()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ReleaseDate FROM Win32_BIOS");
            using var results = searcher.Get();
            foreach (ManagementObject bios in results.Cast<ManagementObject>())
            {
                using (bios)
                {
                    if (bios["ReleaseDate"] is string wmiDate
                        && ManagementDateTimeConverter.ToDateTime(wmiDate) is { } parsed)
                    {
                        return parsed.ToUniversalTime();
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
