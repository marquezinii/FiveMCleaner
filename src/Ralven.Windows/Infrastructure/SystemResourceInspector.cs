using System.Runtime.InteropServices;

namespace Ralven.Windows.Infrastructure;

public sealed record SystemResourceSnapshot(
    long TotalMemoryBytes,
    long AvailableMemoryBytes,
    int LogicalProcessorCount,
    long SystemDriveFreeBytes,
    long CommitLimitBytes,
    long AvailableCommitBytes);

public interface ISystemResourceInspector
{
    SystemResourceSnapshot GetSnapshot();
}

/// <summary>
/// Reads passive, read-only capacity signals from the local machine: total and
/// available physical memory, logical processor count, free space on the
/// Windows system drive and the commit values exposed by GlobalMemoryStatusEx.
/// It never writes anything and never starts an external process, PowerShell
/// or WMI query.
/// </summary>
public sealed class WindowsSystemResourceInspector : ISystemResourceInspector
{
    public SystemResourceSnapshot GetSnapshot()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status))
        {
            throw new InvalidOperationException("Windows memory status is unavailable.");
        }

        var systemDrive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
        return new SystemResourceSnapshot(
            checked((long)status.TotalPhysical),
            checked((long)status.AvailablePhysical),
            Math.Max(1, Environment.ProcessorCount),
            systemDrive.AvailableFreeSpace,
            checked((long)status.TotalPageFile),
            checked((long)status.AvailablePageFile));
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
