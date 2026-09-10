using Ralven.Windows.Infrastructure;

namespace Ralven.App.Services;

public sealed record LiveSystemMetricsSnapshot(
    double? CpuPercent,
    double? GpuPercent,
    double? MemoryPercent,
    double? DiskPercent,
    double? NetworkThroughputMBps,
    DateTimeOffset CapturedAt,
    /// <summary>Physical memory in use, in GiB, or null when Windows did not report it.</summary>
    double? UsedMemoryGiB = null,
    /// <summary>Total physical memory, in GiB, or null when Windows did not report it.</summary>
    double? TotalMemoryGiB = null,
    /// <summary>Verified FiveM process count, or null for system/indeterminate readings.</summary>
    int? FiveMProcessCount = null);

public interface ILiveSystemMetricsProvider
{
    Task<LiveSystemMetricsSnapshot> CaptureAsync(CancellationToken cancellationToken = default);

    Task<LiveSystemMetricsSnapshot> CaptureFiveMAsync(
        string installationRoot,
        CancellationToken cancellationToken = default);
}

public sealed class WindowsLiveSystemMetricsProvider : ILiveSystemMetricsProvider, IDisposable
{
    private readonly ISystemResourceInspector systemInspector = new WindowsSystemResourceInspector();
    private readonly WindowsResourceUsageInspector resourceInspector = new();
    private readonly WindowsFiveMResourceUsageInspector fiveMResourceInspector = new();

    public Task<LiveSystemMetricsSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Capture(cancellationToken), cancellationToken);

    public Task<LiveSystemMetricsSnapshot> CaptureFiveMAsync(
        string installationRoot,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => CaptureFiveM(installationRoot, cancellationToken), cancellationToken);

    private LiveSystemMetricsSnapshot Capture(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var usage = resourceInspector.GetSnapshot(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return CreateSnapshot(usage, systemInspector.GetSnapshot(), DateTimeOffset.UtcNow);
    }

    private LiveSystemMetricsSnapshot CaptureFiveM(
        string installationRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var capturedAt = DateTimeOffset.UtcNow;
        var usage = fiveMResourceInspector.GetSnapshot(
            installationRoot,
            capturedAt,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new LiveSystemMetricsSnapshot(
            usage.CpuPercent,
            GpuPercent: null,
            MemoryPercent: null,
            DiskPercent: null,
            NetworkThroughputMBps: null,
            CapturedAt: capturedAt,
            UsedMemoryGiB: usage.WorkingSetGiB,
            TotalMemoryGiB: null,
            FiveMProcessCount: usage.ProcessCount);
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
