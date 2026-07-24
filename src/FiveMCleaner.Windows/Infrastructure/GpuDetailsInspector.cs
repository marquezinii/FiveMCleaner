using Microsoft.Win32;

namespace FiveMCleaner.Windows.Infrastructure;

public enum GpuKindGuess
{
    Unknown,
    LikelyIntegrated,
    LikelyDiscrete
}

public sealed record GpuAdapterDetails(
    string DriverDescription,
    long? VramBytes,
    GpuKindGuess KindGuess);

public interface IGpuDetailsInspector
{
    IReadOnlyList<GpuAdapterDetails> GetSnapshot();
}

/// <summary>
/// Reads VRAM size and a best-effort integrated-vs-discrete classification
/// from the same registry location already used for GPU driver descriptions
/// (SYSTEM\CurrentControlSet\Control\Video). VRAM comes from the
/// HardwareInformation.qwMemorySize value most drivers publish; the
/// integrated/discrete split is a name-based heuristic, not a hardware
/// query, and is presented as a guess rather than a fact.
/// </summary>
public sealed class WindowsGpuDetailsInspector : IGpuDetailsInspector
{
    private static readonly string[] IntegratedMarkers =
    [
        "Intel(R) UHD",
        "Intel(R) HD Graphics",
        "Intel(R) Iris",
        "AMD Radeon(TM) Graphics",
        "AMD Radeon Graphics",
        "Radeon(TM) Vega"
    ];

    public IReadOnlyList<GpuAdapterDetails> GetSnapshot()
    {
        var results = new List<GpuAdapterDetails>();
        try
        {
            using var video = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Video");
            if (video is null)
            {
                return results;
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
                    if (string.IsNullOrWhiteSpace(name)
                        || name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var vram = adapter?.GetValue("HardwareInformation.qwMemorySize") switch
                    {
                        long value and > 0 => value,
                        int value and > 0 => (long)value,
                        _ => (long?)null
                    };

                    results.Add(new GpuAdapterDetails(name, vram, GuessKind(name)));
                }
            }
        }
        catch (Exception exception) when (exception is System.Security.SecurityException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            return [];
        }

        return results;
    }

    private static GpuKindGuess GuessKind(string driverDescription)
    {
        if (IntegratedMarkers.Any(marker =>
                driverDescription.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return GpuKindGuess.LikelyIntegrated;
        }

        if (driverDescription.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
            || driverDescription.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase)
            || driverDescription.Contains("Arc", StringComparison.OrdinalIgnoreCase))
        {
            return GpuKindGuess.LikelyDiscrete;
        }

        return GpuKindGuess.Unknown;
    }
}
