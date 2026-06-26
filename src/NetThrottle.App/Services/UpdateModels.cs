namespace NetThrottle.App.Services;

/// <summary>Outcome of an update check against the GitHub Releases API.</summary>
public sealed class UpdateCheckResult
{
    public bool IsUpdateAvailable { get; init; }
    public Version CurrentVersion { get; init; } = new(0, 0, 0);
    public Version? LatestVersion { get; init; }
    public string? Tag { get; init; }
    public string? ReleaseNotes { get; init; }

    /// <summary>The release page on GitHub (fallback for portable builds).</summary>
    public string? ReleasePageUrl { get; init; }

    /// <summary>Direct download URL of the NetThrottle_vX.Y.Z_Setup.exe asset, if present.</summary>
    public string? SetupAssetUrl { get; init; }
    public string? SetupAssetName { get; init; }

    public static UpdateCheckResult UpToDate(Version current) =>
        new() { IsUpdateAvailable = false, CurrentVersion = current, LatestVersion = current };
}
