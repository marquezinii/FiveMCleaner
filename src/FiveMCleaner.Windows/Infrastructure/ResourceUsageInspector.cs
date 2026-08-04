using System.ComponentModel;
using System.Runtime.InteropServices;
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
/// GPU utilization, and network throughput. CPU and disk use PDH with
/// PdhAddEnglishCounterW (Vista+), which accepts English counter names
/// regardless of the OS language — fixing the localized v1 counter names
/// that fail on non-English Windows (e.g., pt-BR). Network is reported as
/// raw throughput via NetworkInterface statistics.
/// </summary>
public sealed class WindowsResourceUsageInspector : IResourceUsageInspector
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(300);

    public ResourceUsageSnapshot GetSnapshot()
    {
        var cpuTask = Task.Run(() => ReadPdhCounterAsync(
            "\\Processor(_Total)\\% Processor Time"));
        var diskTask = Task.Run(() => ReadPdhCounterAsync(
            "\\PhysicalDisk(_Total)\\% Disk Time"));
        var gpuTask = Task.Run(() => TryReadGpuUsageAsync());
        var networkTask = Task.Run(() => TryReadNetworkThroughputMBpsAsync());

        try
        {
            Task.WaitAll(cpuTask, diskTask, gpuTask, networkTask);
        }
        catch (AggregateException)
        {
        }

        return new ResourceUsageSnapshot(
            cpuTask.IsCompletedSuccessfully ? cpuTask.Result : null,
            diskTask.IsCompletedSuccessfully ? diskTask.Result : null,
            gpuTask.IsCompletedSuccessfully ? gpuTask.Result : null,
            networkTask.IsCompletedSuccessfully ? networkTask.Result : 0);
    }

    private static async Task<double?> ReadPdhCounterAsync(string englishCounterPath)
    {
        IntPtr queryHandle = IntPtr.Zero;
        IntPtr counterHandle = IntPtr.Zero;
        try
        {
            if (PdhOpenQueryW(IntPtr.Zero, UIntPtr.Zero, out queryHandle) != 0)
            {
                return null;
            }

            var addStatus = PdhAddEnglishCounterW(queryHandle, englishCounterPath, UIntPtr.Zero, out counterHandle);
            if (addStatus != 0)
            {
                return null;
            }

            var collectStatus = PdhCollectQueryData(queryHandle);
            if (collectStatus != 0)
            {
                return null;
            }

            await Task.Delay(SampleInterval).ConfigureAwait(false);

            collectStatus = PdhCollectQueryData(queryHandle);
            if (collectStatus != 0)
            {
                return null;
            }

            var formatStatus = PdhGetFormattedCounterValue(counterHandle, PDH_FMT_DOUBLE, out _, out var value);
            if (formatStatus != 0)
            {
                return null;
            }

            return Math.Clamp(value.DoubleValue, 0, 100);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or UnauthorizedAccessException
            or Win32Exception)
        {
            return null;
        }
        finally
        {
            if (queryHandle != IntPtr.Zero)
            {
                PdhCloseQuery(queryHandle);
            }
        }
    }

    private static async Task<double?> TryReadGpuUsageAsync()
    {
        try
        {
            if (!System.Diagnostics.PerformanceCounterCategory.Exists("GPU Engine"))
            {
                return null;
            }

            var category = new System.Diagnostics.PerformanceCounterCategory("GPU Engine");
            var instances = category.GetInstanceNames()
                .Where(name => name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (instances.Length == 0)
            {
                return null;
            }

            var counters = instances
                .Select(instance => new System.Diagnostics.PerformanceCounter(
                    "GPU Engine", "Utilization Percentage", instance, true))
                .ToArray();
            try
            {
                foreach (var counter in counters)
                {
                    counter.NextValue();
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

    private const int PDH_FMT_DOUBLE = 0x00000200;

    [StructLayout(LayoutKind.Explicit)]
    private struct PdhCounterValue
    {
        [FieldOffset(0)]
        public int LongValue;
        [FieldOffset(0)]
        public double DoubleValue;
        [FieldOffset(0)]
        public long LargeValue;
        [FieldOffset(0)]
        public IntPtr AnsiStringValue;
        [FieldOffset(0)]
        public IntPtr WideStringValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int PdhOpenQueryW(IntPtr dataSource, UIntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int PdhAddEnglishCounterW(IntPtr query, string counterPath, UIntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern int PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern int PdhGetFormattedCounterValue(IntPtr counter, int format, out int type, out PdhCounterValue value);

    [DllImport("pdh.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PdhCloseQuery(IntPtr query);
}
