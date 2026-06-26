using NetThrottle.Core.Models;
using NetThrottle.Engine;

namespace NetThrottle.App.Services;

/// <summary>
/// Thin application-facing wrapper around <see cref="PacketEngine"/> so view
/// models never touch the engine directly.
/// </summary>
public sealed class EngineController : IDisposable
{
    private readonly PacketEngine _engine = new();

    public EngineController()
    {
        _engine.Faulted += ex => Faulted?.Invoke(ex);
    }

    /// <summary>Raised on the engine's pump thread when it dies of a native error.</summary>
    public event Action<Exception>? Faulted;

    public bool IsRunning => _engine.IsRunning;

    public void Start(IEnumerable<ThrottleRule> rules)
    {
        _engine.ApplyRules(rules);
        _engine.Start();
    }

    public void Stop() => _engine.Stop();

    public void ApplyRules(IEnumerable<ThrottleRule> rules) => _engine.ApplyRules(rules);

    public IReadOnlyDictionary<(string Process, Direction Direction), long> SnapshotTraffic() => _engine.SnapshotTraffic();

    public void Dispose() => _engine.Dispose();
}
