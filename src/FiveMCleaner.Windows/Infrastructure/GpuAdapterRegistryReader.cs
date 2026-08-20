using Microsoft.Win32;

namespace FiveMCleaner.Windows.Infrastructure;

internal sealed record GpuRegistryAdapter(
    string DriverDescription,
    long? VramBytes,
    string? MatchingDeviceId);

internal static class GpuAdapterRegistryReader
{
    private const string VideoRegistryPath = @"SYSTEM\CurrentControlSet\Control\Video";

    public static IReadOnlyList<GpuRegistryAdapter> ReadAll()
    {
        var adapters = new List<GpuRegistryAdapter>();
        try
        {
            using var video = Registry.LocalMachine.OpenSubKey(VideoRegistryPath);
            if (video is null)
            {
                return adapters;
            }

            foreach (var deviceKeyName in video.GetSubKeyNames())
            {
                using var device = video.OpenSubKey(deviceKeyName);
                if (device is null)
                {
                    continue;
                }

                foreach (var adapterKeyName in device.GetSubKeyNames().Where(IsAdapterSubKey))
                {
                    using var adapter = device.OpenSubKey(adapterKeyName);
                    var description = (adapter?.GetValue("DriverDesc") as string)?.Trim();
                    if (string.IsNullOrWhiteSpace(description)
                        || description.Contains("Basic Render", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var vramBytes = adapter?.GetValue("HardwareInformation.qwMemorySize") switch
                    {
                        long value and > 0 => value,
                        int value and > 0 => (long)value,
                        _ => (long?)null
                    };
                    adapters.Add(new GpuRegistryAdapter(
                        description,
                        vramBytes,
                        adapter?.GetValue("MatchingDeviceId") as string));
                }
            }
        }
        catch (Exception exception) when (exception is System.Security.SecurityException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            return [];
        }

        return adapters;
    }

    private static bool IsAdapterSubKey(string name)
    {
        return name.Length == 4 && name.All(char.IsDigit);
    }
}
