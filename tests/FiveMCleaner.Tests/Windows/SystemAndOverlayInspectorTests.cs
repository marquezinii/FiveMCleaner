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
}
