using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FiveMCleaner.Windows.Infrastructure;

public enum HardwareGpuSchedulingState
{
    NotSupportedOrUnknown,
    Disabled,
    Enabled
}

public sealed record DisplayConfigurationSnapshot(
    int Width,
    int Height,
    int CurrentRefreshHz,
    int MaxRefreshHzAtCurrentResolution,
    HardwareGpuSchedulingState HardwareGpuScheduling);

public interface IDisplayConfigurationInspector
{
    DisplayConfigurationSnapshot? GetSnapshot();
}

/// <summary>
/// Reads the current display mode and the highest refresh rate the active
/// monitor advertises at that same resolution, using the standard
/// EnumDisplaySettings Win32 API (no vendor SDK, no driver). Also reads
/// Hardware-Accelerated GPU Scheduling from its documented registry value.
/// Variable refresh rate (G-SYNC/FreeSync/VRR) is not exposed by any public,
/// driver-free Windows API, so it is intentionally not reported here rather
/// than guessed.
/// </summary>
public sealed class WindowsDisplayConfigurationInspector : IDisplayConfigurationInspector
{
    private const int EnumCurrentSettings = -1;

    public DisplayConfigurationSnapshot? GetSnapshot()
    {
        var current = new DevMode();
        current.dmSize = (short)Marshal.SizeOf<DevMode>();
        if (!EnumDisplaySettings(null, EnumCurrentSettings, ref current))
        {
            return null;
        }

        var maxRefresh = current.dmDisplayFrequency;
        var mode = new DevMode();
        mode.dmSize = (short)Marshal.SizeOf<DevMode>();
        for (var index = 0; EnumDisplaySettings(null, index, ref mode); index++)
        {
            if (mode.dmPelsWidth == current.dmPelsWidth
                && mode.dmPelsHeight == current.dmPelsHeight
                && mode.dmDisplayFrequency > maxRefresh)
            {
                maxRefresh = mode.dmDisplayFrequency;
            }
        }

        return new DisplayConfigurationSnapshot(
            current.dmPelsWidth,
            current.dmPelsHeight,
            current.dmDisplayFrequency,
            maxRefresh,
            ReadHagsState());
    }

    private static HardwareGpuSchedulingState ReadHagsState()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
            var value = key?.GetValue("HwSchMode");
            return value switch
            {
                int mode and 2 => HardwareGpuSchedulingState.Enabled,
                int mode and 1 => HardwareGpuSchedulingState.Disabled,
                _ => HardwareGpuSchedulingState.NotSupportedOrUnknown
            };
        }
        catch (Exception exception) when (exception is System.Security.SecurityException
            or UnauthorizedAccessException)
        {
            return HardwareGpuSchedulingState.NotSupportedOrUnknown;
        }
    }

    // Explicitly Unicode end-to-end (entry point + struct marshaling). Modern
    // Windows only exports the wide EnumDisplaySettingsW variant meaningfully;
    // pairing it with CharSet.Auto/Ansi struct layout silently misaligns every
    // field after dmDeviceName and yields a "successful" call with all-zero
    // resolution/refresh data instead of a clean failure.
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplaySettingsW")]
    private static extern bool EnumDisplaySettings(
        string? deviceName,
        int modeNum,
        ref DevMode devMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }
}
