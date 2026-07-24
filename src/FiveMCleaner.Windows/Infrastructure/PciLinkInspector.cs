using System.Runtime.InteropServices;

namespace FiveMCleaner.Windows.Infrastructure;

public sealed record PciLinkSnapshot(
    string AdapterName,
    int? CurrentLinkWidth,
    int? CurrentLinkSpeedGtPerSecondTimesTen,
    int? MaxLinkWidth,
    int? MaxLinkSpeedGtPerSecondTimesTen);

public interface IPciLinkInspector
{
    IReadOnlyList<PciLinkSnapshot> GetSnapshot();
}

/// <summary>
/// Best-effort read of the current and maximum PCIe link width/speed for
/// display adapters, via the documented CM_Get_DevNode_Property device
/// property API (cfgmgr32.dll) and the standard PCI device DEVPKEYs — no
/// vendor SDK, no driver. A missing or mismatched property simply reports
/// "not found" rather than returning unrelated data, so a wrong result here
/// degrades to "unavailable", never to a plausible-looking wrong number.
/// </summary>
public sealed class WindowsPciLinkInspector : IPciLinkInspector
{
    // {3ab22e31-8264-4b4e-9af5-a8d2d8e33e62}, pid 4..7 — DEVPKEY_PciDevice_*Link*.
    private static readonly Guid PciDeviceFmtId = new("3ab22e31-8264-4b4e-9af5-a8d2d8e33e62");
    private const int DevpropTypeUint32 = 0x00000007;
    private const uint CrSuccess = 0;
    private const uint CmLocateDevnodeNormal = 0;

    public IReadOnlyList<PciLinkSnapshot> GetSnapshot()
    {
        var results = new List<PciLinkSnapshot>();
        try
        {
            using var video = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Video");
            if (video is null)
            {
                return results;
            }

            foreach (var deviceKeyName in video.GetSubKeyNames())
            {
                using var device = video.OpenSubKey(deviceKeyName);
                if (device is null)
                {
                    continue;
                }

                foreach (var adapterKeyName in device.GetSubKeyNames()
                             .Where(name => name.Length == 4 && name.All(char.IsDigit)))
                {
                    using var adapter = device.OpenSubKey(adapterKeyName);
                    var name = (adapter?.GetValue("DriverDesc") as string)?.Trim();
                    var matchingDeviceId = adapter?.GetValue("MatchingDeviceId") as string;
                    if (string.IsNullOrWhiteSpace(name)
                        || name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var snapshot = TryReadLinkInfo(name, matchingDeviceId);
                    if (snapshot is not null)
                    {
                        results.Add(snapshot);
                    }
                }
            }
        }
        catch (Exception exception) when (exception is System.Security.SecurityException
            or UnauthorizedAccessException)
        {
            return [];
        }

        return results;
    }

    private static PciLinkSnapshot? TryReadLinkInfo(string adapterName, string? matchingDeviceId)
    {
        if (string.IsNullOrWhiteSpace(matchingDeviceId))
        {
            return null;
        }

        try
        {
            if (CM_Locate_DevNode(out var devInst, matchingDeviceId, CmLocateDevnodeNormal) != CrSuccess)
            {
                return null;
            }

            var current = ReadUInt32Property(devInst, PciDeviceFmtId, 6);
            var currentWidth = ReadUInt32Property(devInst, PciDeviceFmtId, 7);
            var max = ReadUInt32Property(devInst, PciDeviceFmtId, 4);
            var maxWidth = ReadUInt32Property(devInst, PciDeviceFmtId, 5);
            if (current is null && currentWidth is null && max is null && maxWidth is null)
            {
                return null;
            }

            return new PciLinkSnapshot(
                adapterName,
                (int?)currentWidth,
                (int?)current,
                (int?)maxWidth,
                (int?)max);
        }
        catch (Exception exception) when (exception is EntryPointNotFoundException or DllNotFoundException)
        {
            return null;
        }
    }

    private static uint? ReadUInt32Property(uint devInst, Guid fmtId, uint pid)
    {
        var propertyKey = new DevPropKey { fmtid = fmtId, pid = pid };
        uint bufferSize = 4;
        var buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            var result = CM_Get_DevNode_Property(
                devInst,
                ref propertyKey,
                out var propertyType,
                buffer,
                ref bufferSize,
                0);
            if (result != CrSuccess || propertyType != DevpropTypeUint32)
            {
                return null;
            }

            return unchecked((uint)Marshal.ReadInt32(buffer));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey
    {
        public Guid fmtid;
        public uint pid;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Locate_DevNodeW")]
    private static extern uint CM_Locate_DevNode(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_PropertyW")]
    private static extern uint CM_Get_DevNode_Property(
        uint devInst,
        ref DevPropKey propertyKey,
        out uint propertyType,
        IntPtr propertyBuffer,
        ref uint propertyBufferSize,
        uint flags);
}
