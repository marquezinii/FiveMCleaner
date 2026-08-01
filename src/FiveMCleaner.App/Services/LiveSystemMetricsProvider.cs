using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using FiveMCleaner.Windows.Infrastructure;

namespace FiveMCleaner.App.Services;

public sealed record LiveSystemMetricsSnapshot(
    double? CpuPercent,
    double? GpuPercent,
    double? MemoryPercent,
    double? DiskPercent,
    double NetworkThroughputMBps,
    DateTimeOffset CapturedAt);

public interface ILiveSystemMetricsProvider
{
    Task<LiveSystemMetricsSnapshot> CaptureAsync(CancellationToken cancellationToken = default);
}

public sealed class WindowsLiveSystemMetricsProvider : ILiveSystemMetricsProvider, IDisposable
{
    private readonly object sync = new();
    private readonly ISystemResourceInspector systemInspector = new WindowsSystemResourceInspector();
    private PerformanceCounter? cpuCounter;
    private PerformanceCounter? diskCounter;
    private PerformanceCounter[] gpuCounters = [];
    private long? previousNetworkBytes;
    private long previousNetworkTimestamp;
    private double? lastGpuUsage;
    private int samplesUntilGpuRefresh;
    private bool initialized;
    private bool disposed;

    public Task<LiveSystemMetricsSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Capture(cancellationToken), cancellationToken);

    private LiveSystemMetricsSnapshot Capture(CancellationToken cancellationToken)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureInitialized();

            var usage = new ResourceUsageSnapshot(
                ReadCounter(cpuCounter),
                ReadCounter(diskCounter),
                ReadGpuUsageWhenDue(),
                ReadNetworkThroughput());
            return CreateSnapshot(usage, systemInspector.GetSnapshot(), DateTimeOffset.Now);
        }
    }

    internal static LiveSystemMetricsSnapshot CreateSnapshot(
        ResourceUsageSnapshot usage,
        SystemResourceSnapshot system,
        DateTimeOffset capturedAt)
    {
        double? memoryPercent = system.TotalMemoryBytes > 0
            ? 100d * (system.TotalMemoryBytes - system.AvailableMemoryBytes) / system.TotalMemoryBytes
            : null;

        return new LiveSystemMetricsSnapshot(
            usage.CpuPercent,
            usage.GpuPercent,
            memoryPercent is { } value ? Math.Clamp(value, 0, 100) : null,
            usage.DiskPercent,
            usage.NetworkThroughputMBps,
            capturedAt);
    }

    private void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        cpuCounter = CreateCounter("Processor", "% Processor Time", "_Total");
        diskCounter = CreateCounter("PhysicalDisk", "% Disk Time", "_Total");
        gpuCounters = CreateGpuCounters();
        Prime(cpuCounter);
        Prime(diskCounter);
        foreach (var counter in gpuCounters)
        {
            Prime(counter);
        }

        previousNetworkBytes = ReadNetworkBytes();
        previousNetworkTimestamp = Stopwatch.GetTimestamp();
    }

    private static PerformanceCounter? CreateCounter(string category, string counter, string instance)
    {
        try
        {
            return new PerformanceCounter(category, counter, instance, readOnly: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or UnauthorizedAccessException
            or Win32Exception)
        {
            return null;
        }
    }

    private static PerformanceCounter[] CreateGpuCounters()
    {
        var counters = new List<PerformanceCounter>();
        try
        {
            if (!PerformanceCounterCategory.Exists("GPU Engine"))
            {
                return [];
            }

            var instances = new PerformanceCounterCategory("GPU Engine")
                .GetInstanceNames()
                .Where(name => name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase));
            foreach (var instance in instances)
            {
                counters.Add(new PerformanceCounter(
                    "GPU Engine",
                    "Utilization Percentage",
                    instance,
                    readOnly: true));
            }

            return [.. counters];
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or UnauthorizedAccessException
            or Win32Exception)
        {
            foreach (var counter in counters)
            {
                counter.Dispose();
            }

            return [];
        }
    }

    private static void Prime(PerformanceCounter? counter)
    {
        try
        {
            counter?.NextValue();
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or UnauthorizedAccessException
            or Win32Exception)
        {
        }
    }

    private static double? ReadCounter(PerformanceCounter? counter)
    {
        try
        {
            return counter is null ? null : Math.Clamp(counter.NextValue(), 0, 100);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or UnauthorizedAccessException
            or Win32Exception)
        {
            return null;
        }
    }

    private double? ReadGpuUsage()
    {
        if (gpuCounters.Length == 0)
        {
            return null;
        }

        var values = gpuCounters.Select(ReadCounter).Where(value => value is not null).ToArray();
        return values.Length == 0 ? null : Math.Clamp(values.Sum(value => value!.Value), 0, 100);
    }

    private double? ReadGpuUsageWhenDue()
    {
        if (samplesUntilGpuRefresh > 0)
        {
            samplesUntilGpuRefresh--;
            return lastGpuUsage;
        }

        lastGpuUsage = ReadGpuUsage();
        samplesUntilGpuRefresh = 2;
        return lastGpuUsage;
    }

    private double ReadNetworkThroughput()
    {
        var currentBytes = ReadNetworkBytes();
        var currentTimestamp = Stopwatch.GetTimestamp();
        var elapsedSeconds = Stopwatch.GetElapsedTime(previousNetworkTimestamp, currentTimestamp).TotalSeconds;
        var throughput = previousNetworkBytes is { } previous
            && currentBytes is { } current
            && elapsedSeconds > 0
                ? Math.Max(0, (current - previous) / elapsedSeconds / (1024d * 1024d))
                : 0;
        previousNetworkBytes = currentBytes;
        previousNetworkTimestamp = currentTimestamp;
        return throughput;
    }

    private static long? ReadNetworkBytes()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(network => network.OperationalStatus == OperationalStatus.Up
                    && network.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Sum(network =>
                {
                    var statistics = network.GetIPStatistics();
                    return statistics.BytesReceived + statistics.BytesSent;
                });
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            cpuCounter?.Dispose();
            diskCounter?.Dispose();
            foreach (var counter in gpuCounters)
            {
                counter.Dispose();
            }
        }
    }
}
