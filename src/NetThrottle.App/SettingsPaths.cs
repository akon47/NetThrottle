using System.IO;

namespace NetThrottle.App;

/// <summary>
/// Decides where settings live. A portable build ships a marker file named
/// <c>portable.marker</c> next to the executable; when present, settings are
/// stored alongside the executable so the whole folder stays self-contained and
/// movable. Otherwise (installed build) settings go under %AppData%\NetThrottle.
/// </summary>
public static class SettingsPaths
{
    private const string PortableMarker = "portable.marker";
    private const string FileName = "settings.json";

    public static string AppDir => AppContext.BaseDirectory;

    public static bool IsPortable => File.Exists(Path.Combine(AppDir, PortableMarker));

    public static string SettingsFile => IsPortable
        ? Path.Combine(AppDir, FileName)
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NetThrottle", FileName);
}
