using FiveMCleaner.Windows.Infrastructure;

namespace FiveMCleaner.App.Services;

public sealed record LiveSystemMetricsSnapshot(
    double? CpuPercent,
    double? GpuPercent,
    double? MemoryPercent,
    double? DiskPercent,
    double NetworkThroughputMBps,
    DateTimeOffset CapturedAt,
    /// <summary>Physical memory in use, in GiB, or null when Windows did not report it.</summary>
    double? UsedMemoryGiB = null,
    /// <summary>Total physical memory, in GiB, or null when Windows did not report it.</summary>
    double? TotalMemoryGiB = null);

public interface ILiveSystemMetricsProvider
{
    Task<LiveSystemMetricsSnapshot> CaptureAsync(CancellationToken cancellationToken = default);
}

public sealed class WindowsLiveSystemMetricsProvider : ILiveSystemMetricsProvider, IDisposable
{
    private readonly ISystemResourceInspector systemInspector = new WindowsSystemResourceInspector();
    private readonly IResourceUsageInspector resourceInspector = new WindowsResourceUsageInspector();

    public Task<LiveSystemMetricsSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Capture(cancellationToken), cancellationToken);

    private LiveSystemMetricsSnapshot Capture(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var usage = resourceInspector.GetSnapshot();
        return CreateSnapshot(usage, systemInspector.GetSnapshot(), DateTimeOffset.UtcNow);
    }

    internal static LiveSystemMetricsSnapshot CreateSnapshot(
        ResourceUsageSnapshot usage,
        SystemResourceSnapshot system,
        DateTimeOffset capturedAt)
    {
        double? memoryPercent = system.TotalMemoryBytes > 0
            ? 100d * (system.TotalMemoryBytes - system.AvailableMemoryBytes) / system.TotalMemoryBytes
            : null;

        const double bytesPerGiB = 1024d * 1024 * 1024;
        double? totalMemoryGiB = system.TotalMemoryBytes > 0
            ? system.TotalMemoryBytes / bytesPerGiB
            : null;
        double? usedMemoryGiB = totalMemoryGiB is null
            ? null
            : Math.Max(0, (system.TotalMemoryBytes - system.AvailableMemoryBytes) / bytesPerGiB);

        return new LiveSystemMetricsSnapshot(
            usage.CpuPercent,
            usage.GpuPercent,
            memoryPercent is { } value ? Math.Clamp(value, 0, 100) : null,
            usage.DiskPercent,
            usage.NetworkThroughputMBps,
            capturedAt,
            usedMemoryGiB,
            totalMemoryGiB);
    }

    public void Dispose()
    {
        // No unmanaged resources to release; kept for interface compatibility.
    }
}
