using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Ralven.Windows.Infrastructure;

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

    public ResourceUsageSnapshot GetSnapshot() => GetSnapshot(CancellationToken.None);

    public ResourceUsageSnapshot GetSnapshot(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cpuTask = Task.Run(() => ReadPdhCounterAsync(
            "\\Processor(_Total)\\% Processor Time", cancellationToken));
        var diskTask = Task.Run(() => ReadPdhCounterAsync(
            "\\PhysicalDisk(_Total)\\% Disk Time", cancellationToken));
        var gpuTask = Task.Run(() => TryReadGpuUsageAsync(cancellationToken));
        var networkTask = Task.Run(() => TryReadNetworkThroughputMBpsAsync(cancellationToken));

        try
        {
            Task.WaitAll(cpuTask, diskTask, gpuTask, networkTask);
        }
        catch (AggregateException)
        {
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new ResourceUsageSnapshot(
            cpuTask.IsCompletedSuccessfully ? cpuTask.Result : null,
            diskTask.IsCompletedSuccessfully ? diskTask.Result : null,
            gpuTask.IsCompletedSuccessfully ? gpuTask.Result : null,
            networkTask.IsCompletedSuccessfully ? networkTask.Result : 0);
    }

    private static async Task<double?> ReadPdhCounterAsync(string englishCounterPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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

            await Task.Delay(SampleInterval, cancellationToken).ConfigureAwait(false);

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

    private static async Task<double?> TryReadGpuUsageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            gpuEngineCategoryExists ??= PerformanceCounterCategory.Exists("GPU Engine");
            if (!gpuEngineCategoryExists.Value)
            {
                return null;
            }

            // NextValue reads the category for each instance. Read it once per
            // sample instead: GPU Engine can contain hundreds of instances.
            var category = new PerformanceCounterCategory("GPU Engine");
            var before = ReadGpuSamples(category);
            if (before.Count == 0)
            {
                return null;
            }

            await Task.Delay(SampleInterval, cancellationToken).ConfigureAwait(false);
            return CalculateGpuUsage(before, ReadGpuSamples(category));
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or UnauthorizedAccessException
            or Win32Exception)
        {
            return null;
        }
    }

    private static Dictionary<string, CounterSample> ReadGpuSamples(PerformanceCounterCategory category) =>
        category.ReadCategory()["Utilization Percentage"].Values.Cast<InstanceData>()
            .Where(instance => instance.InstanceName.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(instance => instance.InstanceName, instance => instance.Sample, StringComparer.OrdinalIgnoreCase);

    internal static double? CalculateGpuUsage(
        IReadOnlyDictionary<string, CounterSample> before,
        IReadOnlyDictionary<string, CounterSample> after)
    {
        double total = 0;
        var hasSample = false;
        foreach (var (name, current) in after)
        {
            // New/disappeared engines have no comparable pair; never reuse a
            // sample from a different instance or report fabricated zero usage.
            if (before.TryGetValue(name, out var previous))
            {
                var value = CounterSample.Calculate(previous, current);
                if (float.IsFinite(value))
                {
                    total += Math.Max(0, value);
                    hasSample = true;
                }
            }
        }

        return hasSample ? Math.Clamp(total, 0, 100) : null;
    }

    private static async Task<double> TryReadNetworkThroughputMBpsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
            var elapsed = Stopwatch.StartNew();
            await Task.Delay(SampleInterval, cancellationToken).ConfigureAwait(false);
            var after = Sample();
            var bytesPerSecond = (after - before) / elapsed.Elapsed.TotalSeconds;
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
