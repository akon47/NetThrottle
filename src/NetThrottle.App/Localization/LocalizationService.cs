using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace NetThrottle.App.Localization;

/// <summary>A selectable language: <c>Code</c> is the locale file name, <c>Name</c> the display label.</summary>
public sealed record LanguageOption(string Code, string Name);

/// <summary>
/// String catalog loaded from <c>locales/*.json</c> next to the executable. Each
/// JSON is a flat "key": "text" map (plus a "_name" display label); the file name
/// is the language code. Adding a language is just dropping in another JSON file.
/// Lookups fall back to English and finally to the key itself, so missing strings
/// never crash the UI. Bound in XAML via the <c>{loc:Tr Key}</c> markup extension.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Instance { get; } = new();

    private readonly Dictionary<string, Dictionary<string, string>> _byCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _english = BuiltInEnglish();
    private Dictionary<string, string> _current;

    private LocalizationService() => _current = _english;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the active language changes, so code-built strings can refresh.</summary>
    public event Action? LanguageChanged;

    public IReadOnlyList<LanguageOption> Languages { get; private set; } = new List<LanguageOption> { new("en", "English") };

    public string CurrentCode { get; private set; } = "en";

    /// <summary>Translated text for <paramref name="key"/>, falling back to English then the key.</summary>
    public string this[string key] =>
        _current.TryGetValue(key, out var v) ? v :
        _english.TryGetValue(key, out var e) ? e : key;

    public string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, this[key], args);

    /// <summary>Loads the locale files and selects the saved/OS/English language.</summary>
    public void Initialize(string localesDir, string? preferredCode)
    {
        LoadLocales(localesDir);
        Apply(Pick(preferredCode), notify: false);
    }

    public void SetLanguage(string code)
    {
        if (string.Equals(code, CurrentCode, StringComparison.OrdinalIgnoreCase)) return;
        Apply(code, notify: true);
    }

    private void Apply(string code, bool notify)
    {
        _current = _byCode.TryGetValue(code, out var dict) ? dict : _english;
        CurrentCode = _byCode.ContainsKey(code) ? code : "en";
        if (notify)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            LanguageChanged?.Invoke();
        }
    }

    private string Pick(string? preferred)
    {
        if (!string.IsNullOrWhiteSpace(preferred) && _byCode.ContainsKey(preferred)) return preferred!;
        string os = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        if (_byCode.ContainsKey(os)) return os;
        if (_byCode.ContainsKey("en")) return "en";
        return _byCode.Keys.FirstOrDefault() ?? "en";
    }

    private void LoadLocales(string dir)
    {
        _byCode["en"] = _english; // built-in English is always present

        try
        {
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
                {
                    try
                    {
                        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
                        if (dict is { Count: > 0 })
                            _byCode[Path.GetFileNameWithoutExtension(file).ToLowerInvariant()] = dict;
                    }
                    catch
                    {
                        // Ignore a malformed locale file rather than failing startup.
                    }
                }
            }
        }
        catch
        {
            // No locales directory / not readable: English-only.
        }

        Languages = _byCode
            .Select(kv => new LanguageOption(kv.Key,
                kv.Value.TryGetValue("_name", out var n) && !string.IsNullOrWhiteSpace(n) ? n : kv.Key))
            .OrderBy(l => l.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    private static Dictionary<string, string> BuiltInEnglish() => new(StringComparer.Ordinal)
    {
        ["_name"] = "English",
        ["Toolbar.Start"] = "Start",
        ["Toolbar.Stop"] = "Stop",
        ["Toolbar.Filter"] = "Filter:",
        ["Toolbar.LimitedOnly"] = "Limited only",
        ["Toolbar.RefreshApps"] = "Refresh apps",
        ["Toolbar.CheckUpdates"] = "Check updates",
        ["Toolbar.Settings"] = "Settings",
        ["Toolbar.Language"] = "Language:",
        ["Settings.Title"] = "Settings",
        ["Settings.Language"] = "Language",
        ["Settings.General"] = "General",
        ["Settings.Startup"] = "Startup",
        ["Settings.Display"] = "Display",
        ["Settings.RunAtStartup"] = "Start with Windows",
        ["Settings.StartMinimized"] = "Start minimized to tray",
        ["Settings.UseMegabytes"] = "Show limits in MB/s",
        ["Settings.Close"] = "Close",
        ["Status.Running"] = "Running — limits are live",
        ["Status.Stopped"] = "Stopped",
        ["Status.Mode"] = "Mode:",
        ["Col.Process"] = "Process (image name)",
        ["Col.Protocol"] = "Protocol",
        ["Col.DownKBps"] = "Down KB/s",
        ["Col.UpKBps"] = "Up KB/s",
        ["Col.Down"] = "Down",
        ["Col.Up"] = "Up",
        ["Col.Live"] = "live",
        ["Col.Clear"] = "Clear",
        ["Update.Now"] = "Update now",
        ["Update.Skip"] = "Skip",
        ["Update.Downloading"] = "Downloading update…",
        ["Update.DownloadingFormat"] = "Downloading update… {0}",
        ["Update.AvailableFormat"] = "Update available: v{0} (current v{1})",
        ["Tooltip.NotRunning"] = "Not running — the limit re-applies automatically when it starts again.",
        ["Tooltip.DownLimit"] = "Download limit · 0 = unlimited",
        ["Tooltip.UpLimit"] = "Upload limit · 0 = unlimited",
        ["Tooltip.DownLive"] = "Live download rate",
        ["Tooltip.UpLive"] = "Live upload rate",
        ["Tray.Open"] = "Open NetThrottle",
        ["Tray.Exit"] = "Exit",
        ["Tray.Hint"] = "Still running in the tray — limits stay active. Right-click the icon to exit.",
        ["Msg.LatestFormat"] = "You are on the latest version ({0}).",
        ["Msg.UpdateCheckFailedFormat"] = "Update check failed: {0}",
        ["Msg.UpdateFailedFormat"] = "Update failed: {0}",
        ["Msg.EngineFailed"] = "Failed to start the throttling engine.\n\nMake sure WinDivert.dll / WinDivert64.sys are next to the app and that you are running as administrator.\n\n{0}",
        ["Msg.EngineStopped"] = "The throttling engine stopped unexpectedly:\n\n{0}",
    };
}
