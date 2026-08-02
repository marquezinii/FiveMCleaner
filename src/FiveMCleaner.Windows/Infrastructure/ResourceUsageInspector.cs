using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace FiveMCleaner.Windows.Infrastructure;

public sealed record ResourceUsageSnapshot(
    double? CpuPercent,
    double? DiskPercent,
    double? GpuPercent,
    double NetworkThroughputMBps);

public interface IResourceUsageInspector
{
    ResourceUsageSnapshot GetSnapshot();
}

/// <summary>
/// Takes a short (roughly 300ms) two-sample reading of CPU, physical disk,
/// GPU utilization, and network throughput via the standard PerformanceCounter API.
/// All readings are performed concurrently to minimize total sampling time (~300ms total
/// instead of ~900ms sequential). Network is reported as raw throughput rather than
/// a percentage, since adapter link speed is not reliably available to compute a
/// meaningful utilization percentage on every adapter.
/// </summary>
public sealed class WindowsResourceUsageInspector : IResourceUsageInspector
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(300);

    public ResourceUsageSnapshot GetSnapshot()
    {
        // Run all measurements concurrently to reduce total sampling time from ~900ms to ~300ms
        var cpuTask = Task.Run(() => TryReadCounterAsync());
        var diskTask = Task.Run(() => TryReadCounterAsync("PhysicalDisk", "% Disk Time", "_Total"));
        var gpuTask = Task.Run(() => TryReadGpuUsageAsync());
        var networkTask = Task.Run(() => TryReadNetworkThroughputMBpsAsync());

        Task.WaitAll(cpuTask, diskTask, gpuTask, networkTask);

        return new ResourceUsageSnapshot(cpuTask.Result, diskTask.Result, gpuTask.Result, networkTask.Result);
    }

    private static async Task<double?> TryReadCounterAsync(string category = "Processor", string counter = "% Processor Time", string instance = "_Total")
    {
        try
        {
            using var performanceCounter = new PerformanceCounter(category, counter, instance, true);
            performanceCounter.NextValue(); // Prime the counter
            await Task.Delay(SampleInterval).ConfigureAwait(false);
            return Math.Clamp(performanceCounter.NextValue(), 0, 100);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or UnauthorizedAccessException
            or Win32Exception)
        {
            return null;
        }
    }

    private static async Task<double?> TryReadGpuUsageAsync()
    {
        try
        {
            if (!PerformanceCounterCategory.Exists("GPU Engine"))
            {
                return null;
            }

            var category = new PerformanceCounterCategory("GPU Engine");
            var instances = category.GetInstanceNames()
                .Where(name => name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (instances.Length == 0)
            {
                return null;
            }

            var counters = instances
                .Select(instance => new PerformanceCounter(
                    "GPU Engine", "Utilization Percentage", instance, true))
                .ToArray();
            try
            {
                foreach (var counter in counters)
                {
                    counter.NextValue(); // Prime
                }

                await Task.Delay(SampleInterval).ConfigureAwait(false);
                var total = counters.Sum(counter => counter.NextValue());
                return Math.Clamp(total, 0, 100);
            }
            finally
            {
                foreach (var counter in counters)
                {
                    counter.Dispose();
                }
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or UnauthorizedAccessException
            or Win32Exception)
        {
            return null;
        }
    }

    private static async Task<double> TryReadNetworkThroughputMBpsAsync()
    {
        try
        {
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                    && nic.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                .ToArray();

            long Sample() => interfaces.Sum(nic =>
            {
                var stats = nic.GetIPStatistics();
                return stats.BytesReceived + stats.BytesSent;
            });

            var before = Sample();
            await Task.Delay(SampleInterval).ConfigureAwait(false);
            var after = Sample();
            var bytesPerSecond = (after - before) / SampleInterval.TotalSeconds;
            return Math.Max(0, bytesPerSecond / (1024d * 1024d));
        }
        catch (System.Net.NetworkInformation.NetworkInformationException)
        {
            return 0;
        }
    }
}
