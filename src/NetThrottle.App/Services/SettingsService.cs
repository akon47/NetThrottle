using NetThrottle.Core.Settings;

namespace NetThrottle.App.Services;

public interface ISettingsService
{
    AppSettings Current { get; }
    string Path { get; }
    void Save();
}

/// <summary>
/// Holds the loaded <see cref="AppSettings"/> and persists them to the location
/// chosen by <see cref="SettingsPaths"/> (next to the exe when portable).
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private readonly SettingsStore _store;

    public SettingsService()
    {
        _store = new SettingsStore(SettingsPaths.SettingsFile);
        Current = _store.Load();
    }

    public AppSettings Current { get; }

    public string Path => _store.Path;

    public void Save() => _store.Save(Current);
}
