using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NetThrottle.Core.Models;
using NetThrottle.Core.RateLimiting;
using NetThrottle.Engine.Native;

namespace NetThrottle.Engine;

/// <summary>
/// The traffic-shaping core. It runs in one of two modes:
///
/// * <b>Sniff</b> (no active limits): a copy of each TCP/UDP packet is read for
///   live statistics, but packets are NOT diverted or re-injected — so there is
///   essentially no impact on throughput.
/// * <b>Divert</b> (at least one limit): packets are intercepted at the network
///   layer, attributed to a process, and released through a per-(process,
///   direction) <see cref="TokenBucket"/>. Nothing is dropped — packets are only
///   delayed to honor the configured average rate.
///
/// WinDivert filters by port/IP (not by process), so a single handle carries all
/// TCP/UDP traffic; a pool of pump threads drains it in parallel (Recv/Send are
/// thread-safe) to spread the work across CPU cores while throttling. The mode
/// switches automatically as limits are added or cleared, so simply turning the
/// engine on with no limits does not slow the network down.
/// </summary>
public sealed class PacketEngine : IDisposable
{
    private const string Filter = "tcp or udp";

    private static readonly IEqualityComparer<(string Process, Direction Direction)> KeyCmp = new ProcessDirectionComparer();
    private static readonly int AddressSize = Marshal.SizeOf<WinDivertNative.Address>();

    private readonly object _gate = new();
    private readonly ProcessPortMap _portMap = new();
    private readonly ConcurrentDictionary<(string Process, Direction Direction), TokenBucket> _buckets = new(KeyCmp);
    private readonly ConcurrentDictionary<(string Process, Direction Direction), long> _traffic = new(KeyCmp);
    private readonly List<Thread> _pumps = new();

    private volatile IReadOnlyList<ThrottleRule> _rules = Array.Empty<ThrottleRule>();
    private nint _handle = WinDivertNative.InvalidHandle;
    private volatile bool _running;
    private volatile bool _closing;
    private bool _diverting;

    /// <summary>Raised when a pump thread terminates because of a native error.</summary>
    public event Action<Exception>? Faulted;

    public bool IsRunning => _running;

    /// <summary>Cumulative bytes seen per (process, direction). The UI samples this
    /// on a timer and differentiates it into a live rate.</summary>
    public IReadOnlyDictionary<(string Process, Direction Direction), long> SnapshotTraffic() =>
        new Dictionary<(string Process, Direction Direction), long>(_traffic, KeyCmp);

    /// <summary>Replace the active rule set. Safe to call while running; switches the
    /// capture mode between sniff and divert as limits appear or disappear.</summary>
    public void ApplyRules(IEnumerable<ThrottleRule> rules)
    {
        var list = rules.Where(r => r.Enabled).ToList();
        _rules = list;

        foreach (var rule in list)
        {
            UpsertBucket(rule.ProcessName, Direction.Inbound, rule.DownloadBytesPerSec);
            UpsertBucket(rule.ProcessName, Direction.Outbound, rule.UploadBytesPerSec);
        }

        lock (_gate)
        {
            if (_running && NeedDivert() != _diverting)
                ReopenPumpLocked(NeedDivert());
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_running) return;
            WinDivertNative.EnsureInitialized();
            _running = true;
            OpenPumpLocked(NeedDivert());
        }
    }

    public void Stop()
    {
        Thread[] pumps;
        lock (_gate)
        {
            if (!_running) return;
            _running = false;
            pumps = ClosePumpLocked();
        }
        JoinAll(pumps);
    }

    private bool NeedDivert()
    {
        foreach (var rule in _rules)
            if (rule.DownloadBytesPerSec > 0 || rule.UploadBytesPerSec > 0)
                return true;
        return false;
    }

    private void ReopenPumpLocked(bool divert)
    {
        JoinAll(ClosePumpLocked());
        try
        {
            OpenPumpLocked(divert);
        }
        catch (Exception ex)
        {
            _running = false;
            Faulted?.Invoke(ex);
        }
    }

    private void OpenPumpLocked(bool divert)
    {
        var flags = divert
            ? WinDivertNative.OpenFlags.None
            : WinDivertNative.OpenFlags.Sniff | WinDivertNative.OpenFlags.RecvOnly;

        nint handle = WinDivertNative.WinDivertOpen(Filter, WinDivertNative.Layer.Network, 0, flags);
        if (handle == WinDivertNative.InvalidHandle)
            throw new InvalidOperationException(
                "WinDivertOpen failed. Ensure WinDivert.dll/.sys are present and the app runs as administrator. " +
                $"Win32 error {Marshal.GetLastWin32Error()}.");

        if (divert)
        {
            // Bigger queues ride out bursts without dropping while shaping.
            WinDivertNative.WinDivertSetParam(handle, WinDivertNative.Param.QueueLength, 16384);
            WinDivertNative.WinDivertSetParam(handle, WinDivertNative.Param.QueueSize, 33554432);
            WinDivertNative.WinDivertSetParam(handle, WinDivertNative.Param.QueueTime, 2000);
        }

        _handle = handle;
        _diverting = divert;
        _closing = false;

        // One thread is plenty for sniffing; use a small pool to parallelize the
        // heavier divert path (parse + attribute + re-inject) across cores.
        int count = divert ? Math.Clamp(Environment.ProcessorCount - 1, 2, 8) : 1;
        _pumps.Clear();
        for (int i = 0; i < count; i++)
        {
            var thread = new Thread(() => PumpLoop(handle, divert))
            {
                IsBackground = true,
                Name = (divert ? "NetThrottle.Divert." : "NetThrottle.Sniff.") + i,
            };
            _pumps.Add(thread);
            thread.Start();
        }
    }

    /// <summary>Closes the handle (unblocking every Recv) and returns the pump threads to join outside the lock.</summary>
    private Thread[] ClosePumpLocked()
    {
        Thread[] pumps = _pumps.ToArray();
        _pumps.Clear();
        if (_handle != WinDivertNative.InvalidHandle)
        {
            _closing = true;
            WinDivertNative.WinDivertClose(_handle);
            _handle = WinDivertNative.InvalidHandle;
        }
        return pumps;
    }

    private static void JoinAll(Thread[]? pumps)
    {
        if (pumps is null) return;
        foreach (var t in pumps)
            t.Join(TimeSpan.FromSeconds(2));
    }

    private void PumpLoop(nint handle, bool divert)
    {
        const int bufferSize = 262144; // 256 KB holds many packets per syscall
        const int maxPackets = 255;    // WinDivert batch limit

        var buffer = new byte[bufferSize];
        var addrs = new WinDivertNative.Address[maxPackets];
        byte[]? sendBuffer = divert ? new byte[bufferSize] : null;
        var sendAddrs = divert ? new WinDivertNative.Address[maxPackets] : null;

        try
        {
            while (true)
            {
                uint addrLen = (uint)(addrs.Length * AddressSize);
                if (!WinDivertNative.WinDivertRecvEx(handle, buffer, (uint)buffer.Length, out uint recvLen,
                        0, addrs, ref addrLen, nint.Zero))
                    break; // handle closed (mode switch / stop) or a fatal error

                int count = (int)(addrLen / (uint)AddressSize);
                int offset = 0, sendBytes = 0, sendCount = 0;

                for (int i = 0; i < count && offset < recvLen; i++)
                {
                    int plen = PacketParser.TotalLength(buffer.AsSpan(offset, (int)recvLen - offset));
                    if (plen <= 0 || offset + plen > recvLen) break;

                    TimeSpan delay = Process(buffer.AsSpan(offset, plen), addrs[i], divert);

                    if (divert)
                    {
                        if (delay <= TimeSpan.Zero)
                        {
                            // Re-inject immediately as part of one batched send.
                            Array.Copy(buffer, offset, sendBuffer!, sendBytes, plen);
                            sendAddrs![sendCount++] = addrs[i];
                            sendBytes += plen;
                        }
                        else
                        {
                            ScheduleSend(handle, buffer.AsSpan(offset, plen).ToArray(), (uint)plen, addrs[i], delay);
                        }
                    }

                    offset += plen;
                }

                // Sniff mode only observes copies — the originals were never removed,
                // so nothing is sent back there.
                if (divert && sendCount > 0)
                    WinDivertNative.WinDivertSendEx(handle, sendBuffer!, (uint)sendBytes, out _,
                        0, sendAddrs!, (uint)(sendCount * AddressSize), nint.Zero);
            }
        }
        catch (Exception ex) when (_running && !_closing)
        {
            _running = false;
            Faulted?.Invoke(ex);
        }
    }

    private TimeSpan Process(ReadOnlySpan<byte> packet, WinDivertNative.Address addr, bool divert)
    {
        if (!PacketParser.TryParse(packet, out var info))
            return TimeSpan.Zero;

        var direction = addr.IsOutbound ? Direction.Outbound : Direction.Inbound;
        ushort localPort = addr.IsOutbound ? info.SrcPort : info.DstPort;
        string? process = _portMap.ResolveProcessName(info.Protocol, localPort);
        if (process is not { Length: > 0 })
            return TimeSpan.Zero;

        var key = (process, direction);
        long len = packet.Length;
        _traffic.AddOrUpdate(key, static (_, l) => l, static (_, v, l) => v + l, len);

        if (divert &&
            FindRule(process, info.Protocol) is { } rule &&
            rule.LimitsDirection(direction) &&
            _buckets.TryGetValue(key, out var bucket))
            return bucket.Reserve(len);

        return TimeSpan.Zero;
    }

    private ThrottleRule? FindRule(string process, ProtocolKind protocol)
    {
        foreach (var rule in _rules)
        {
            if (!rule.ProcessName.Equals(process, StringComparison.OrdinalIgnoreCase)) continue;
            if (!rule.Protocol.HasFlag(protocol)) continue;
            return rule;
        }
        return null;
    }

    private void ScheduleSend(nint handle, byte[] packet, uint len, WinDivertNative.Address addr, TimeSpan delay)
    {
        var copy = packet.AsSpan(0, (int)len).ToArray(); // pump buffer is reused on the next Recv
        var addrCopy = addr;
        _ = Task.Delay(delay).ContinueWith(_ => SendPacket(handle, copy, len, addrCopy), TaskScheduler.Default);
    }

    private static void SendPacket(nint handle, byte[] packet, uint len, WinDivertNative.Address addr)
    {
        if (handle == WinDivertNative.InvalidHandle) return;
        WinDivertNative.WinDivertSend(handle, packet, len, out _, ref addr);
    }

    private void UpsertBucket(string process, Direction direction, long ratePerSec)
    {
        if (string.IsNullOrWhiteSpace(process) || ratePerSec <= 0) return;
        _buckets.AddOrUpdate((process, direction),
            _ => new TokenBucket(ratePerSec),
            (_, existing) => { existing.UpdateRate(ratePerSec); return existing; });
    }

    /// <summary>Case-insensitive on the process name; allocation-free (no per-packet key string).</summary>
    private sealed class ProcessDirectionComparer : IEqualityComparer<(string Process, Direction Direction)>
    {
        public bool Equals((string Process, Direction Direction) x, (string Process, Direction Direction) y)
            => x.Direction == y.Direction && string.Equals(x.Process, y.Process, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Process, Direction Direction) k)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(k.Process), (int)k.Direction);
    }

    public void Dispose() => Stop();
}
