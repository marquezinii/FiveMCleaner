using Ralven.Windows.Infrastructure;
using Xunit;

namespace Ralven.Tests.Windows;

public sealed class MouseAccelerationInspectorTests
{
    [Fact]
    public void GetSnapshot_ReturnsAllValuesFromSpiGetMouse()
    {
        var inspector = new WindowsMouseAccelerationInspector(values =>
        {
            values[0] = 6;
            values[1] = 10;
            values[2] = 1;
            return true;
        });

        var snapshot = inspector.GetSnapshot();

        Assert.Equal(MouseAccelerationInspectionState.Available, snapshot.State);
        Assert.Equal(6, snapshot.Threshold1);
        Assert.Equal(10, snapshot.Threshold2);
        Assert.Equal(1, snapshot.AccelerationLevel);
    }

    [Fact]
    public void GetSnapshot_ReportsUnavailableWhenTheNativeReadFails()
    {
        var inspector = new WindowsMouseAccelerationInspector(_ => false);

        var snapshot = inspector.GetSnapshot();

        Assert.Equal(MouseAccelerationSnapshot.Unavailable, snapshot);
    }

    [Fact]
    public void GetSnapshot_ReportsUnavailableWhenTheNativeReadThrows()
    {
        var inspector = new WindowsMouseAccelerationInspector(
            _ => throw new InvalidOperationException("native failure"));

        var snapshot = inspector.GetSnapshot();

        Assert.Equal(MouseAccelerationSnapshot.Unavailable, snapshot);
    }

    [Fact]
    public void Set_WritesTheThreeDocumentedMouseValues()
    {
        int[]? written = null;
        var controller = new WindowsMouseAccelerationInspector(
            _ => true,
            values =>
            {
                written = values;
                return true;
            });

        controller.Set(0, 0, 0);

        Assert.NotNull(written);
        Assert.Equal([0, 0, 0], written!);
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.Set(0, 0, 3));
    }
}
