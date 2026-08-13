using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

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
    private static bool? gpuEngineCategoryExists;

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

            // Só PDH_CSTATUS_VALID_DATA e PDH_CSTATUS_NEW_DATA trazem um valor
            // real; qualquer outro status deixa a união com lixo.
            if (value.CStatus is not (PDH_CSTATUS_VALID_DATA or PDH_CSTATUS_NEW_DATA))
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
            gpuEngineCategoryExists ??= PerformanceCounterCategory.Exists("GPU Engine");
            if (!gpuEngineCategoryExists.Value)
            {
                return null;
            }

            var instances = new PerformanceCounterCategory("GPU Engine").GetInstanceNames()
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
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                    && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
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
        catch (NetworkInformationException)
        {
            return 0;
        }
    }

    private const int PDH_FMT_DOUBLE = 0x00000200;
    private const int PDH_CSTATUS_VALID_DATA = 0x00000000;
    private const int PDH_CSTATUS_NEW_DATA = 0x00000001;

    /// <summary>
    /// Espelha <c>PDH_FMT_COUNTERVALUE</c>: um <c>DWORD CStatus</c> seguido da
    /// união do valor, que o compilador alinha em 8 bytes por causa de
    /// <c>double</c>/<c>LONGLONG</c>. Sem o <c>CStatus</c>, a união caía sobre
    /// ele e toda leitura de CPU e disco voltava exatamente 0. Só o braço
    /// <c>double</c> é declarado, porque as leituras aqui sempre pedem
    /// <c>PDH_FMT_DOUBLE</c>; os demais braços da união nativa têm o mesmo
    /// tamanho e não alterariam o layout.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct PdhCounterValue
    {
        [FieldOffset(0)]
        public int CStatus;
        [FieldOffset(8)]
        public double DoubleValue;
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
