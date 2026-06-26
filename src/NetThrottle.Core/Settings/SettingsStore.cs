using System.Text.Json;

namespace NetThrottle.Core.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON at a caller-supplied path.
/// The path is decided by the host (portable build → next to the executable,
/// installed build → %AppData%), keeping this class free of environment logic.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string Path { get; }

    public SettingsStore(string path) => Path = path;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(Path)) return new AppSettings();
            string json = File.ReadAllText(Path);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        string? dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string json = JsonSerializer.Serialize(settings, Options);
        File.WriteAllText(Path, json);
    }
}
