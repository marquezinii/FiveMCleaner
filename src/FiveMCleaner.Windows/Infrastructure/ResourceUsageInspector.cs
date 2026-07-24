using System.ComponentModel;
using System.Diagnostics;

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
/// Takes a short (roughly 300ms) two-sample reading of CPU and physical disk
/// utilization via the standard PerformanceCounter API, plus a best-effort
/// GPU utilization read from the "GPU Engine" counter category Windows has
/// exposed natively since 10 1803 (no vendor SDK, no driver). Network is
/// reported as raw throughput rather than a percentage, since adapter link
/// speed is not reliably available to compute a meaningful utilization
/// percentage on every adapter. A single snapshot is an instantaneous
/// sample, not an average — presented that way, not as a trend.
/// </summary>
public sealed class WindowsResourceUsageInspector : IResourceUsageInspector
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(300);

    public ResourceUsageSnapshot GetSnapshot()
    {
        var cpu = TryReadCounter("Processor", "% Processor Time", "_Total");
        var disk = TryReadCounter("PhysicalDisk", "% Disk Time", "_Total");
        var gpu = TryReadGpuUsage();
        var network = TryReadNetworkThroughputMBps();
        return new ResourceUsageSnapshot(cpu, disk, gpu, network);
    }

    private static double? TryReadCounter(string category, string counter, string instance)
    {
        try
        {
            using var performanceCounter = new PerformanceCounter(category, counter, instance, true);
            performanceCounter.NextValue();
            Thread.Sleep(SampleInterval);
            return Math.Clamp(performanceCounter.NextValue(), 0, 100);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or UnauthorizedAccessException
            or Win32Exception)
        {
            return null;
        }
    }

    private static double? TryReadGpuUsage()
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
                    counter.NextValue();
                }

                Thread.Sleep(SampleInterval);
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

    private static double TryReadNetworkThroughputMBps()
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
            Thread.Sleep(SampleInterval);
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
