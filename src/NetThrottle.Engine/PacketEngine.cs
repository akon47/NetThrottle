using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NetThrottle.Core.Models;
using NetThrottle.Core.RateLimiting;
using NetThrottle.Engine.Native;

namespace NetThrottle.Engine;

/// <summary>
/// The traffic-shaping core. Opens a WinDivert handle, reads packets on a
/// dedicated pump thread, attributes each to a process, and releases it through
/// a per-(process, direction) <see cref="TokenBucket"/>. Packets are never
/// dropped — they are delayed to honor the configured average rate.
/// </summary>
public sealed class PacketEngine : IDisposable
{
    private readonly object _gate = new();
    private readonly ProcessPortMap _portMap = new();
    private readonly ConcurrentDictionary<string, TokenBucket> _buckets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _traffic = new(StringComparer.OrdinalIgnoreCase);

    private volatile IReadOnlyList<ThrottleRule> _rules = Array.Empty<ThrottleRule>();
    private nint _handle = WinDivertNative.InvalidHandle;
    private Thread? _pump;
    private volatile bool _running;

    /// <summary>Raised when the pump thread terminates because of a native error.</summary>
    public event Action<Exception>? Faulted;

    public bool IsRunning => _running;

    /// <summary>
    /// Cumulative bytes seen per "process|direction" key for processes that match
    /// a rule. The UI samples this on a timer and differentiates it into a rate.
    /// </summary>
    public IReadOnlyDictionary<string, long> SnapshotTraffic() => new Dictionary<string, long>(_traffic);

    /// <summary>Builds the dictionary key used by both buckets and traffic counters.</summary>
    public static string TrafficKey(string process, Direction direction) => BucketKey(process, direction);

    /// <summary>Replace the active rule set. Safe to call while running.</summary>
    public void ApplyRules(IEnumerable<ThrottleRule> rules)
    {
        var list = rules.Where(r => r.Enabled).ToList();
        _rules = list;

        foreach (var rule in list)
        {
            UpsertBucket(rule.ProcessName, Direction.Inbound, rule.DownloadBytesPerSec);
            UpsertBucket(rule.ProcessName, Direction.Outbound, rule.UploadBytesPerSec);
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_running) return;

            WinDivertNative.EnsureInitialized();

            // Capture only TCP/UDP over IPv4/IPv6 at the network layer.
            const string filter = "tcp or udp";
            _handle = WinDivertNative.WinDivertOpen(filter, WinDivertNative.Layer.Network, 0,
                WinDivertNative.OpenFlags.None);
            if (_handle == WinDivertNative.InvalidHandle)
                throw new InvalidOperationException(
                    "WinDivertOpen failed. Ensure WinDivert.dll/.sys are present and the app runs as administrator. " +
                    $"Win32 error {Marshal.GetLastWin32Error()}.");

            _running = true;
            _pump = new Thread(PumpLoop) { IsBackground = true, Name = "NetThrottle.Pump" };
            _pump.Start();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_running) return;
            _running = false;
            if (_handle != WinDivertNative.InvalidHandle)
            {
                WinDivertNative.WinDivertClose(_handle);
                _handle = WinDivertNative.InvalidHandle;
            }
        }
        _pump?.Join(TimeSpan.FromSeconds(2));
        _pump = null;
    }

    private void PumpLoop()
    {
        var packet = new byte[65535];
        var addr = new WinDivertNative.Address();
        try
        {
            while (_running)
            {
                if (!WinDivertNative.WinDivertRecv(_handle, packet, (uint)packet.Length, out uint len, ref addr))
                {
                    if (!_running) break;
                    continue; // transient; keep pumping
                }

                ReleasePacket(packet, len, addr);
            }
        }
        catch (Exception ex) when (_running)
        {
            _running = false;
            Faulted?.Invoke(ex);
        }
    }

    private void ReleasePacket(byte[] packet, uint len, WinDivertNative.Address addr)
    {
        TimeSpan delay = TimeSpan.Zero;

        if (PacketParser.TryParse(packet, len, out var info))
        {
            var direction = addr.IsOutbound ? Direction.Outbound : Direction.Inbound;
            ushort localPort = addr.IsOutbound ? info.SrcPort : info.DstPort;
            string? process = _portMap.ResolveProcessName(info.Protocol, localPort);

            if (process is { Length: > 0 })
            {
                // Count traffic for every process (not just throttled ones) so the
                // UI can show live throughput for the whole running-process list.
                _traffic.AddOrUpdate(BucketKey(process, direction), len, (_, v) => v + len);

                if (FindRule(process, info.Protocol) is { } rule &&
                    rule.LimitsDirection(direction) &&
                    _buckets.TryGetValue(BucketKey(process, direction), out var bucket))
                    delay = bucket.Reserve(len);
            }
        }

        if (delay <= TimeSpan.Zero)
            SendPacket(packet, len, addr);
        else
            ScheduleSend(packet, len, addr, delay);
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

    private void ScheduleSend(byte[] packet, uint len, WinDivertNative.Address addr, TimeSpan delay)
    {
        // Copy: the pump's buffer is reused on the next Recv.
        var copy = packet.AsSpan(0, (int)len).ToArray();
        var addrCopy = addr;
        _ = Task.Delay(delay).ContinueWith(_ =>
        {
            if (_running) SendPacket(copy, len, addrCopy);
        }, TaskScheduler.Default);
    }

    private void SendPacket(byte[] packet, uint len, WinDivertNative.Address addr)
    {
        if (_handle == WinDivertNative.InvalidHandle) return;
        WinDivertNative.WinDivertSend(_handle, packet, len, out _, ref addr);
    }

    private void UpsertBucket(string process, Direction direction, long ratePerSec)
    {
        if (string.IsNullOrWhiteSpace(process) || ratePerSec <= 0) return;
        string key = BucketKey(process, direction);
        _buckets.AddOrUpdate(key,
            _ => new TokenBucket(ratePerSec),
            (_, existing) => { existing.UpdateRate(ratePerSec); return existing; });
    }

    private static string BucketKey(string process, Direction direction) => $"{process}|{direction}";

    public void Dispose() => Stop();
}
