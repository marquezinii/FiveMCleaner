using FiveMCleaner.App.Services;
using FiveMCleaner.Windows.Infrastructure;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class LiveSystemMetricsProviderTests
{
    [Fact]
    public void CreateSnapshot_CombinesRealUsageAndMemorySnapshots()
    {
        var capturedAt = DateTimeOffset.Parse("2026-08-01T12:00:00Z");

        var snapshot = WindowsLiveSystemMetricsProvider.CreateSnapshot(
            new ResourceUsageSnapshot(31, 12, 48, 2.5),
            new SystemResourceSnapshot(
                TotalMemoryBytes: 16L * 1024 * 1024 * 1024,
                AvailableMemoryBytes: 4L * 1024 * 1024 * 1024,
                LogicalProcessorCount: 12,
                SystemDriveFreeBytes: 100L * 1024 * 1024 * 1024,
                TotalPageFileBytes: 20L * 1024 * 1024 * 1024,
                AvailablePageFileBytes: 10L * 1024 * 1024 * 1024),
            capturedAt);

        Assert.Equal(31, snapshot.CpuPercent);
        Assert.Equal(48, snapshot.GpuPercent);
        Assert.Equal(75, snapshot.MemoryPercent);
        Assert.Equal(12, snapshot.DiskPercent);
        Assert.Equal(2.5, snapshot.NetworkThroughputMBps);
        Assert.Equal(capturedAt, snapshot.CapturedAt);
    }
}
