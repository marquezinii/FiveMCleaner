using System.Windows;
using Ralven.App.Controls;
using Xunit;

namespace Ralven.Tests.App;

public sealed class DialogWindowTests
{
    [Theory]
    [InlineData(1920, 1040, 1, 1888, 1008)]
    [InlineData(1920, 1040, 1.5, 1248, 661.333333)]
    [InlineData(1366, 728, 1.25, 1060.8, 550.4)]
    [InlineData(1920, 1040, 2, 928, 488)]
    public void AvailableSize_ConvertsMonitorPixelsToDipsWithAnAccessibleMargin(
        double width, double height, double scale, double expectedWidth, double expectedHeight)
    {
        var size = DialogWindow.AvailableSize(new Size(width, height), new DpiScale(scale, scale));
        Assert.Equal(expectedWidth, size.Width, 5);
        Assert.Equal(expectedHeight, size.Height, 5);
    }
}
