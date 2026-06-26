using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using NetThrottle.Core.Models;
using NetThrottle.Engine;

// Headless end-to-end check for the throttling engine. It transfers bulk data
// over a loopback TCP socket three ways and compares the throughput:
//   1. engine OFF              -> baseline
//   2. engine ON, no limits    -> sniff mode: should stay near baseline
//   3. engine ON, per-proc cap -> divert mode: should collapse to ~the cap
// Must run elevated (WinDivert loads a driver).

string logPath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "smoketest.log");

using var writer = new StreamWriter(logPath, append: false) { AutoFlush = true };
void W(string s)
{
    Console.WriteLine(s);
    writer.WriteLine(s);
}

const long TransferBytes = 32L * 1024 * 1024; // 32 MB
const long CapBytesPerSec = 4L * 1024 * 1024; // 4 MB/s
double capMBs = CapBytesPerSec / 1048576.0;

W("=== NetThrottle smoke test ===");
W($"Time (UTC): {DateTime.UtcNow:O}");

bool admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
W($"Administrator: {admin}");

string baseDir = AppContext.BaseDirectory;
bool hasDll = File.Exists(Path.Combine(baseDir, "WinDivert.dll"));
bool hasSys = File.Exists(Path.Combine(baseDir, "WinDivert64.sys"));
W($"WinDivert.dll present: {hasDll}");
W($"WinDivert64.sys present: {hasSys}");

string me = Process.GetCurrentProcess().ProcessName + ".exe";
W($"This process image name: {me}");
W($"Transfer size: {TransferBytes / 1024 / 1024} MB, cap: {capMBs:0.#} MB/s");
W("");

if (!admin)
{
    W("RESULT: SKIPPED — not elevated. WinDivert needs administrator rights.");
    return 3;
}
if (!hasDll || !hasSys)
{
    W("RESULT: SKIPPED — WinDivert.dll / WinDivert64.sys missing next to the executable.");
    return 3;
}

double baseline = RunLoopbackTransfer(TransferBytes);
W($"1) Baseline (engine OFF):            {baseline,7:0.0} MB/s");

using var engine = new PacketEngine();
try
{
    engine.Start(); // no rules yet -> sniff mode
}
catch (Exception ex)
{
    W($"RESULT: FAIL — engine failed to start: {ex.Message}");
    return 2;
}
Thread.Sleep(800);
double sniff = RunLoopbackTransfer(TransferBytes);
W($"2) Engine ON, no limits (sniff):     {sniff,7:0.0} MB/s");

var rule = new ThrottleRule
{
    Enabled = true,
    ProcessName = me,
    Protocol = ProtocolKind.Both,
    DownloadBytesPerSec = CapBytesPerSec,
    UploadBytesPerSec = CapBytesPerSec,
};
double throttled = RunThrottledTransfer(engine, rule, TransferBytes);
W($"3) Engine ON, {capMBs:0.#} MB/s cap (divert): {throttled,7:0.0} MB/s");

engine.Stop();
W("");

// Loopback runs at multi-GB/s and is purely CPU-bound, so even sniffing's
// per-packet copy costs measurable CPU here. What matters for the real world is
// that sniff sustains far more than any physical link (well over 1 Gbps), i.e.
// the engine is never the bottleneck when no limit is set.
bool sniffOk = sniff > 200; // MB/s  (~1.6 Gbps of headroom)
bool throttleOk = throttled < baseline * 0.25 && throttled <= capMBs * 3.0;
bool pass = sniffOk && throttleOk;

W($"sniff headroom ok ? {sniffOk}   (sniff {sniff:0.0} MB/s, must exceed 200)");
W($"throttled enforced ? {throttleOk}   (throttled {throttled:0.0}, cap {capMBs:0.#})");
W(pass ? "RESULT: PASS" : "RESULT: FAIL");

return pass ? 0 : 1;

static double RunLoopbackTransfer(long bytes)
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;

    var server = Task.Run(() =>
    {
        using TcpClient s = listener.AcceptTcpClient();
        using NetworkStream ns = s.GetStream();
        var buf = new byte[256 * 1024];
        while (ns.Read(buf, 0, buf.Length) > 0) { }
    });

    using var client = new TcpClient();
    client.Connect(IPAddress.Loopback, port);
    using NetworkStream cs = client.GetStream();

    var data = new byte[256 * 1024];
    var sw = Stopwatch.StartNew();
    long sent = 0;
    while (sent < bytes)
    {
        int chunk = (int)Math.Min(data.Length, bytes - sent);
        cs.Write(data, 0, chunk);
        sent += chunk;
    }
    cs.Flush();
    client.Client.Shutdown(SocketShutdown.Send);
    server.Wait();
    sw.Stop();
    listener.Stop();

    return bytes / 1048576.0 / sw.Elapsed.TotalSeconds;
}

// Like RunLoopbackTransfer, but switches the engine to divert mode on an already
// established connection and warms it up so the port→process map attributes it
// before timing (loopback is fast enough to otherwise finish during the lookup gap).
static double RunThrottledTransfer(PacketEngine engine, ThrottleRule rule, long bytes)
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;

    var server = Task.Run(() =>
    {
        using TcpClient s = listener.AcceptTcpClient();
        using NetworkStream ns = s.GetStream();
        var buf = new byte[256 * 1024];
        while (ns.Read(buf, 0, buf.Length) > 0) { }
    });

    using var client = new TcpClient();
    client.Connect(IPAddress.Loopback, port);
    using NetworkStream cs = client.GetStream();

    engine.ApplyRules(new[] { rule }); // a limit appears -> divert mode
    var warm = new byte[256 * 1024];
    for (int i = 0; i < 8; i++) cs.Write(warm, 0, warm.Length); // ~2 MB warmup
    Thread.Sleep(1200); // let the port map refresh and attribute this connection

    var data = new byte[256 * 1024];
    var sw = Stopwatch.StartNew();
    long sent = 0;
    while (sent < bytes)
    {
        int chunk = (int)Math.Min(data.Length, bytes - sent);
        cs.Write(data, 0, chunk);
        sent += chunk;
    }
    cs.Flush();
    client.Client.Shutdown(SocketShutdown.Send);
    server.Wait();
    sw.Stop();
    listener.Stop();

    return bytes / 1048576.0 / sw.Elapsed.TotalSeconds;
}
