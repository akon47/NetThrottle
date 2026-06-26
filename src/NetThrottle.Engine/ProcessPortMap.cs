using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NetThrottle.Core.Models;
using NetThrottle.Engine.Native;

namespace NetThrottle.Engine;

/// <summary>
/// Resolves a (protocol, local port) pair to the owning process name by
/// periodically snapshotting the IPv4 extended TCP/UDP tables. The snapshot is
/// cheap relative to the packet rate, so we refresh on a short interval and let
/// the packet pump read a lock-free dictionary.
/// </summary>
public sealed class ProcessPortMap
{
    private readonly TimeSpan _refreshInterval;
    private readonly ConcurrentDictionary<int, string> _pidToName = new();
    private volatile Dictionary<ushort, int> _tcp = new();
    private volatile Dictionary<ushort, int> _udp = new();
    private long _lastRefreshTicks;

    public ProcessPortMap(TimeSpan? refreshInterval = null)
    {
        _refreshInterval = refreshInterval ?? TimeSpan.FromMilliseconds(250);
        Refresh();
    }

    /// <summary>Process image name (e.g. "chrome.exe") owning the local port, or null.</summary>
    public string? ResolveProcessName(ProtocolKind protocol, ushort localPort)
    {
        MaybeRefresh();
        var map = protocol == ProtocolKind.Udp ? _udp : _tcp;
        if (!map.TryGetValue(localPort, out int pid)) return null;
        return ResolveName(pid);
    }

    private void MaybeRefresh()
    {
        long now = Stopwatch.GetTimestamp();
        double elapsed = (now - Interlocked.Read(ref _lastRefreshTicks)) / (double)Stopwatch.Frequency;
        if (elapsed >= _refreshInterval.TotalSeconds)
            Refresh();
    }

    private void Refresh()
    {
        Interlocked.Exchange(ref _lastRefreshTicks, Stopwatch.GetTimestamp());
        _tcp = SnapshotTcp();
        _udp = SnapshotUdp();
    }

    private static Dictionary<ushort, int> SnapshotTcp()
    {
        var result = new Dictionary<ushort, int>();
        int size = 0;
        IpHelperNative.GetExtendedTcpTable(nint.Zero, ref size, false,
            IpHelperNative.AF_INET, IpHelperNative.TCP_TABLE_OWNER_PID_ALL, 0);
        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (IpHelperNative.GetExtendedTcpTable(buffer, ref size, false,
                    IpHelperNative.AF_INET, IpHelperNative.TCP_TABLE_OWNER_PID_ALL, 0) != 0)
                return result;

            int count = Marshal.ReadInt32(buffer);
            nint rowPtr = buffer + 4;
            int rowSize = Marshal.SizeOf<IpHelperNative.MIB_TCPROW_OWNER_PID>();
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<IpHelperNative.MIB_TCPROW_OWNER_PID>(rowPtr);
                result[IpHelperNative.PortToHost(row.LocalPort)] = (int)row.OwningPid;
                rowPtr += rowSize;
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return result;
    }

    private static Dictionary<ushort, int> SnapshotUdp()
    {
        var result = new Dictionary<ushort, int>();
        int size = 0;
        IpHelperNative.GetExtendedUdpTable(nint.Zero, ref size, false,
            IpHelperNative.AF_INET, IpHelperNative.UDP_TABLE_OWNER_PID, 0);
        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (IpHelperNative.GetExtendedUdpTable(buffer, ref size, false,
                    IpHelperNative.AF_INET, IpHelperNative.UDP_TABLE_OWNER_PID, 0) != 0)
                return result;

            int count = Marshal.ReadInt32(buffer);
            nint rowPtr = buffer + 4;
            int rowSize = Marshal.SizeOf<IpHelperNative.MIB_UDPROW_OWNER_PID>();
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<IpHelperNative.MIB_UDPROW_OWNER_PID>(rowPtr);
                result[IpHelperNative.PortToHost(row.LocalPort)] = (int)row.OwningPid;
                rowPtr += rowSize;
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return result;
    }

    private string ResolveName(int pid)
    {
        if (_pidToName.TryGetValue(pid, out string? name)) return name;
        try
        {
            using var p = Process.GetProcessById(pid);
            name = p.ProcessName + ".exe";
        }
        catch
        {
            name = string.Empty;
        }
        _pidToName[pid] = name;
        return name;
    }
}
