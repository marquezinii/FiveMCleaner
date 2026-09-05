using System.Runtime.InteropServices;

namespace Ralven.Windows.Infrastructure;

public enum MouseAccelerationInspectionState
{
    Available,
    Unavailable
}

public sealed record MouseAccelerationSnapshot(
    MouseAccelerationInspectionState State,
    int? Threshold1,
    int? Threshold2,
    int? AccelerationLevel)
{
    public static MouseAccelerationSnapshot Unavailable { get; } = new(
        MouseAccelerationInspectionState.Unavailable,
        null,
        null,
        null);
}

public interface IMouseAccelerationInspector
{
    MouseAccelerationSnapshot GetSnapshot();
}

public interface IMouseAccelerationController : IMouseAccelerationInspector
{
    void Set(int threshold1, int threshold2, int accelerationLevel);
}

/// <summary>
/// Reads and writes the three values exposed by SPI_GETMOUSE/SPI_SETMOUSE.
/// Read failures remain explicit instead of being inferred as defaults.
/// </summary>
public sealed class WindowsMouseAccelerationInspector : IMouseAccelerationController
{
    private const uint SpiGetMouse = 0x0003;
    private const uint SpiSetMouse = 0x0004;
    private const uint SpifUpdateIniFile = 0x0001;
    private const uint SpifSendChange = 0x0002;

    private readonly Func<int[], bool> readMouse;
    private readonly Func<int[], bool> writeMouse;

    public WindowsMouseAccelerationInspector()
        : this(ReadMouse, WriteMouse)
    {
    }

    internal WindowsMouseAccelerationInspector(Func<int[], bool> readMouse)
        : this(readMouse, WriteMouse)
    {
    }

    internal WindowsMouseAccelerationInspector(
        Func<int[], bool> readMouse,
        Func<int[], bool> writeMouse)
    {
        this.readMouse = readMouse ?? throw new ArgumentNullException(nameof(readMouse));
        this.writeMouse = writeMouse ?? throw new ArgumentNullException(nameof(writeMouse));
    }

    public void Set(int threshold1, int threshold2, int accelerationLevel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(threshold1);
        ArgumentOutOfRangeException.ThrowIfNegative(threshold2);
        if (accelerationLevel is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(accelerationLevel));
        }

        if (!writeMouse([threshold1, threshold2, accelerationLevel]))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public MouseAccelerationSnapshot GetSnapshot()
    {
        var values = new int[3];
        try
        {
            return readMouse(values)
                ? new MouseAccelerationSnapshot(
                    MouseAccelerationInspectionState.Available,
                    values[0],
                    values[1],
                    values[2])
                : MouseAccelerationSnapshot.Unavailable;
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            return MouseAccelerationSnapshot.Unavailable;
        }
    }

    private static bool ReadMouse(int[] values) =>
        SystemParametersInfo(SpiGetMouse, 0, values, 0);

    private static bool WriteMouse(int[] values) =>
        SystemParametersInfo(SpiSetMouse, 0, values, SpifUpdateIniFile | SpifSendChange);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint action,
        uint parameter,
        [In, Out] int[] values,
        uint flags);
}
