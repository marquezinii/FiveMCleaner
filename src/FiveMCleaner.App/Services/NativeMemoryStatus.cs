using System.Runtime.InteropServices;

namespace FiveMCleaner.App.Services;

/// <summary>
/// P/Invoke wrapper for the Windows GlobalMemoryStatusEx API, which returns
/// physical and virtual memory statistics. Kept in a dedicated file to avoid
/// mixing platform interop with application logic in the main service.
/// </summary>
internal static class NativeMemoryStatus
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    public static (ulong TotalPhysical, ulong AvailablePhysical) Query()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status))
        {
            throw new InvalidOperationException("GlobalMemoryStatusEx failed");
        }

        return (status.TotalPhysical, status.AvailablePhysical);
    }

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
