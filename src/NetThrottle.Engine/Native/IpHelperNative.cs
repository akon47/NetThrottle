using System.Runtime.InteropServices;

namespace NetThrottle.Engine.Native;

/// <summary>
/// Minimal IP Helper (iphlpapi.dll) bindings used to map a local port to the
/// owning process id via the extended TCP/UDP tables.
/// </summary>
internal static class IpHelperNative
{
    private const string Dll = "iphlpapi.dll";

    public const int AF_INET = 2;
    public const int AF_INET6 = 23;

    // TCP_TABLE_OWNER_PID_ALL = 5, UDP_TABLE_OWNER_PID = 1
    public const int TCP_TABLE_OWNER_PID_ALL = 5;
    public const int UDP_TABLE_OWNER_PID = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct MIB_TCPROW_OWNER_PID
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;   // network byte order, low 16 bits
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MIB_UDPROW_OWNER_PID
    {
        public uint LocalAddr;
        public uint LocalPort;   // network byte order, low 16 bits
        public uint OwningPid;
    }

    [DllImport(Dll, SetLastError = true)]
    public static extern uint GetExtendedTcpTable(
        nint pTcpTable, ref int dwOutBufLen, bool sort, int ipVersion, int tableClass, uint reserved);

    [DllImport(Dll, SetLastError = true)]
    public static extern uint GetExtendedUdpTable(
        nint pUdpTable, ref int dwOutBufLen, bool sort, int ipVersion, int tableClass, uint reserved);

    /// <summary>WinDivert/IP Helper store ports big-endian; convert the low word to host order.</summary>
    public static ushort PortToHost(uint networkPort)
    {
        ushort p = (ushort)(networkPort & 0xFFFF);
        return (ushort)((p >> 8) | (p << 8));
    }
}
