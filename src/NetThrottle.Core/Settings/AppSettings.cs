using NetThrottle.Core.Models;

namespace NetThrottle.Core.Settings;

/// <summary>Root persisted document: the user's rules plus global state.</summary>
public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public bool EngineEnabled { get; set; }

    /// <summary>A release version the user chose to skip in the update prompt (e.g. "1.2.3").</summary>
    public string? SkippedVersion { get; set; }

    /// <summary>Selected UI language code (locale file name, e.g. "en", "ko"). Null = follow the OS.</summary>
    public string? Language { get; set; }

    /// <summary>Show limits in MB/s instead of KB/s.</summary>
    public bool UnitMegabytes { get; set; }

    /// <summary>Start hidden in the tray instead of showing the window.</summary>
    public bool StartMinimized { get; set; }

    // Remembered window placement (normal-state bounds + maximized flag).
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    public List<ThrottleRule> Rules { get; set; } = new();
}
