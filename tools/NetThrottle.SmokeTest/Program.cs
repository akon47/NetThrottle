using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using NetThrottle.Core.Models;
using NetThrottle.Engine;

// Headless end-to-end check for the throttling engine.
// It transfers bulk data over a loopback TCP socket twice — once with the engine
// off (baseline) and once with a per-process cap applied to THIS process — and
// compares the measured throughput. Must run elevated (WinDivert loads a driver).

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

// 1) Baseline with the engine OFF.
double baseline = RunLoopbackTransfer(TransferBytes);
W($"Baseline throughput (engine OFF): {baseline:0.0} MB/s");

// 2) Same transfer with the engine ON and a cap on THIS process.
var rule = new ThrottleRule
{
    Enabled = true,
    ProcessName = me,
    Protocol = ProtocolKind.Both,
    DownloadBytesPerSec = CapBytesPerSec,
    UploadBytesPerSec = CapBytesPerSec,
};

using var engine = new PacketEngine();
engine.ApplyRules(new[] { rule });
try
{
    engine.Start();
}
catch (Exception ex)
{
    W($"RESULT: FAIL — engine failed to start: {ex.Message}");
    return 2;
}

W("Engine started; warming up port→PID map…");
Thread.Sleep(800);

double throttled = RunLoopbackTransfer(TransferBytes);
W($"Throttled throughput (engine ON, cap {capMBs:0.#} MB/s): {throttled:0.0} MB/s");

engine.Stop();
W("Engine stopped.");
W("");

// Verdict: the capped run must be both far below the baseline and near the cap.
bool clearlyThrottled = throttled < baseline * 0.25;
bool nearCap = throttled <= capMBs * 3.0;
bool pass = clearlyThrottled && nearCap;

W($"baseline*0.25 = {baseline * 0.25:0.0} MB/s, cap*3 = {capMBs * 3.0:0.0} MB/s");
W(pass
    ? $"RESULT: PASS — throttling works. {baseline:0.0} MB/s → {throttled:0.0} MB/s (~{baseline / Math.Max(throttled, 0.01):0}x slower)."
    : $"RESULT: FAIL — cap not enforced as expected (baseline {baseline:0.0}, throttled {throttled:0.0}).");

return pass ? 0 : 1;

// Bulk-sends TransferBytes over a fresh loopback TCP connection and returns MB/s.
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
