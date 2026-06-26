namespace NetThrottle.Core.Models;

/// <summary>
/// A user-defined rate limit targeting a process. A rule with a null/0 limit
/// means "unlimited" for that direction.
/// </summary>
public sealed class ThrottleRule
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Whether the rule is currently applied by the engine.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Process image name to match, e.g. "chrome.exe". Case-insensitive.
    /// Matching by name (rather than PID) so the rule survives restarts.
    /// </summary>
    public string ProcessName { get; set; } = string.Empty;

    public ProtocolKind Protocol { get; set; } = ProtocolKind.Both;

    /// <summary>Download (inbound) cap in bytes/sec. 0 = unlimited.</summary>
    public long DownloadBytesPerSec { get; set; }

    /// <summary>Upload (outbound) cap in bytes/sec. 0 = unlimited.</summary>
    public long UploadBytesPerSec { get; set; }

    public bool LimitsDirection(Direction direction) => direction switch
    {
        Direction.Inbound => DownloadBytesPerSec > 0,
        Direction.Outbound => UploadBytesPerSec > 0,
        _ => DownloadBytesPerSec > 0 || UploadBytesPerSec > 0,
    };

    public long LimitFor(Direction direction) =>
        direction == Direction.Inbound ? DownloadBytesPerSec : UploadBytesPerSec;
}
