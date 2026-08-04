using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
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
    private readonly IResourceUsageInspector resourceInspector = new WindowsResourceUsageInspector();
    private bool disposed;

    public Task<LiveSystemMetricsSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Capture(cancellationToken), cancellationToken);

    private LiveSystemMetricsSnapshot Capture(CancellationToken cancellationToken)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();

            // Use the unified resource inspector which now runs all measurements concurrently
            var usage = resourceInspector.GetSnapshot();
            return CreateSnapshot(usage, systemInspector.GetSnapshot(), DateTimeOffset.UtcNow);
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

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            // No PerformanceCounter instances to dispose since we use the unified inspector
        }
    }
}
