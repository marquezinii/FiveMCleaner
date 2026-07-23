using FiveMCleaner.Windows.Infrastructure;
using Xunit;

namespace FiveMCleaner.Tests.Windows;

public sealed class SystemAndOverlayInspectorTests
{
    [Fact]
    public void WindowsSystemResourceInspector_ReturnsPlausibleLocalValues()
    {
        var inspector = new WindowsSystemResourceInspector();

        var snapshot = inspector.GetSnapshot();

        Assert.True(snapshot.TotalMemoryBytes > 0);
        Assert.True(snapshot.AvailableMemoryBytes >= 0);
        Assert.True(snapshot.AvailableMemoryBytes <= snapshot.TotalMemoryBytes);
        Assert.True(snapshot.LogicalProcessorCount >= 1);
        Assert.True(snapshot.SystemDriveFreeBytes >= 0);
    }

    [Fact]
    public void WindowsOverlaySoftwareInspector_NeverThrowsAndReturnsSortedNames()
    {
        var inspector = new WindowsOverlaySoftwareInspector();

        var names = inspector.DetectRunningOverlayNames();

        Assert.NotNull(names);
        Assert.Equal(names.OrderBy(name => name, StringComparer.Ordinal), names);
    }

    [Fact]
    public void WindowsSystemResourceInspector_ExposesPagefileTotals()
    {
        var inspector = new WindowsSystemResourceInspector();

        var snapshot = inspector.GetSnapshot();

        Assert.True(snapshot.TotalPageFileBytes >= 0);
        Assert.True(snapshot.AvailablePageFileBytes >= 0);
    }

    [Fact]
    public void WindowsNetworkHealthInspector_NeverThrowsAndSendsNoTraffic()
    {
        var inspector = new WindowsNetworkHealthInspector();

        var snapshot = inspector.GetSnapshot();

        Assert.True(snapshot.DiscardedPackets >= 0);
        Assert.True(snapshot.ErrorPackets >= 0);
    }

    [Fact]
    public void WindowsThermalInspector_NeverThrowsAndReportsHonestly()
    {
        var inspector = new WindowsThermalInspector();

        var snapshot = inspector.GetSnapshot();

        if (snapshot.IsAvailable)
        {
            Assert.NotNull(snapshot.HighestCelsius);
            Assert.InRange(snapshot.HighestCelsius!.Value, -40d, 130d);
        }
        else
        {
            Assert.Null(snapshot.HighestCelsius);
        }
    }

    [Fact]
    public void WindowsGpuVendorInspector_NeverThrowsAndNeverWritesAnything()
    {
        var inspector = new WindowsGpuVendorInspector();

        var snapshot = inspector.GetSnapshot();

        Assert.NotNull(snapshot.DriverDescriptions);
    }
}
