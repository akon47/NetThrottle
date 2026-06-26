using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;
using NetThrottle.App.Common;
using NetThrottle.App.Services;
using NetThrottle.Core.Models;
using NetThrottle.Engine;

namespace NetThrottle.App.ViewModels;

/// <summary>The single window's view model: owns the rule list, the engine toggle,
/// live-rate sampling, and the update banner.</summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly EngineController _engine;
    private readonly ISettingsService _settings;
    private readonly IProcessListProvider _processes;
    private readonly IUpdateService _updates;
    private readonly DispatcherTimer _statsTimer;

    private IReadOnlyDictionary<string, long> _lastTraffic = new Dictionary<string, long>();
    private long _lastSampleTicks;
    private bool _engineEnabled;
    private string _statusText = "Stopped";
    private bool _isBusy;
    private bool _suppressPersist;

    private bool _isUpdateAvailable;
    private string _updateText = string.Empty;
    private UpdateCheckResult? _pendingUpdate;

    public MainViewModel(
        EngineController engine,
        ISettingsService settings,
        IProcessListProvider processes,
        IUpdateService updates)
    {
        _engine = engine;
        _settings = settings;
        _processes = processes;
        _updates = updates;

        _engine.Faulted += OnEngineFaulted;

        Rules = new ObservableCollection<RuleViewModel>();
        _suppressPersist = true;
        foreach (var rule in _settings.Current.Rules)
            Rules.Add(Track(new RuleViewModel(rule)));
        _suppressPersist = false;

        Rules.CollectionChanged += OnRulesCollectionChanged;
        _engineEnabled = _settings.Current.EngineEnabled;

        RefreshProcesses();

        ToggleEngineCommand = new RelayCommand(() => ToggleEngine());
        AddRuleCommand = new RelayCommand(AddRule);
        RemoveRuleCommand = new RelayCommand(p => { if (p is RuleViewModel r) RemoveRule(r); });
        RefreshProcessesCommand = new RelayCommand(RefreshProcesses);
        CheckUpdateCommand = new RelayCommand(() => _ = CheckForUpdatesAsync(silent: false));
        ApplyUpdateCommand = new RelayCommand(() => _ = ApplyUpdateAsync());
        SkipUpdateCommand = new RelayCommand(SkipUpdate);

        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statsTimer.Tick += (_, _) => SampleTraffic();
        _statsTimer.Start();

        // Restore the previous run's engine state.
        if (_engineEnabled)
            ToggleEngine(forceOn: true);
    }

    public ObservableCollection<RuleViewModel> Rules { get; }

    public Array ProtocolOptions { get; } = Enum.GetValues(typeof(ProtocolKind));

    public ObservableCollection<string> Processes { get; } = new();

    public bool IsPortable => SettingsPaths.IsPortable;

    public string ModeText => SettingsPaths.IsPortable ? "Portable" : "Installed";

    public string SettingsLocation => _settings.Path;

    public string AppVersion =>
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    public ICommand ToggleEngineCommand { get; }
    public ICommand AddRuleCommand { get; }
    public ICommand RemoveRuleCommand { get; }
    public ICommand RefreshProcessesCommand { get; }
    public ICommand CheckUpdateCommand { get; }
    public ICommand ApplyUpdateCommand { get; }
    public ICommand SkipUpdateCommand { get; }

    /// <summary>Surfaced to the view (code-behind) so it can show a message box.</summary>
    public event Action<string>? Notification;

    public bool EngineEnabled
    {
        get => _engineEnabled;
        set { if (SetProperty(ref _engineEnabled, value)) StatusText = value ? "Running" : "Stopped"; }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        private set => SetProperty(ref _isUpdateAvailable, value);
    }

    public string UpdateText
    {
        get => _updateText;
        private set => SetProperty(ref _updateText, value);
    }

    public void RefreshProcesses()
    {
        var names = _processes.GetRunningProcessNames();
        Processes.Clear();
        foreach (var n in names) Processes.Add(n);
    }

    public async Task CheckForUpdatesAsync(bool silent)
    {
        try
        {
            UpdateCheckResult result = await _updates.CheckAsync().ConfigureAwait(true);

            if (!result.IsUpdateAvailable)
            {
                _pendingUpdate = null;
                IsUpdateAvailable = false;
                if (!silent) Notification?.Invoke($"You are on the latest version ({result.CurrentVersion.ToString(3)}).");
                return;
            }

            if (silent && string.Equals(_settings.Current.SkippedVersion, result.LatestVersion?.ToString(), StringComparison.Ordinal))
                return; // user dismissed this version

            _pendingUpdate = result;
            IsUpdateAvailable = true;
            UpdateText = $"Update available: v{result.LatestVersion?.ToString(3)} (current v{result.CurrentVersion.ToString(3)})";
        }
        catch (Exception ex)
        {
            if (!silent) Notification?.Invoke($"Update check failed: {ex.Message}");
        }
    }

    private void ToggleEngine(bool forceOn = false)
    {
        bool turnOn = forceOn || !_engine.IsRunning;
        try
        {
            if (turnOn)
            {
                _engine.Start(CurrentModels());
                EngineEnabled = true;
            }
            else
            {
                _engine.Stop();
                EngineEnabled = false;
            }
            Persist();
        }
        catch (Exception ex)
        {
            EngineEnabled = false;
            Persist();
            Notification?.Invoke(
                "Failed to start the throttling engine.\n\n" +
                "Make sure WinDivert.dll / WinDivert64.sys are next to the app and that you are running as administrator.\n\n" +
                ex.Message);
        }
    }

    private void AddRule()
    {
        var rule = new ThrottleRule { ProcessName = string.Empty, Protocol = ProtocolKind.Both };
        Rules.Add(Track(new RuleViewModel(rule)));
    }

    private void RemoveRule(RuleViewModel rule) => Rules.Remove(rule);

    private RuleViewModel Track(RuleViewModel vm)
    {
        vm.Changed += OnRuleChanged;
        return vm;
    }

    private void OnRuleChanged()
    {
        Persist();
        if (_engine.IsRunning) _engine.ApplyRules(CurrentModels());
    }

    private void OnRulesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (RuleViewModel vm in e.OldItems) vm.Changed -= OnRuleChanged;
        OnRuleChanged();
    }

    private IReadOnlyList<ThrottleRule> CurrentModels() => Rules.Select(r => r.Model).ToList();

    private void Persist()
    {
        if (_suppressPersist) return;
        _settings.Current.Rules = CurrentModels().ToList();
        _settings.Current.EngineEnabled = EngineEnabled;
        _settings.Save();
    }

    private void SampleTraffic()
    {
        long now = Stopwatch.GetTimestamp();
        var snapshot = _engine.IsRunning ? _engine.SnapshotTraffic() : new Dictionary<string, long>();
        double elapsed = _lastSampleTicks == 0 ? 1 : (now - _lastSampleTicks) / (double)Stopwatch.Frequency;
        if (elapsed <= 0) elapsed = 1;

        foreach (var rule in Rules)
        {
            if (!_engine.IsRunning || string.IsNullOrWhiteSpace(rule.ProcessName))
            {
                rule.CurrentDownload = "—";
                rule.CurrentUpload = "—";
                continue;
            }

            rule.CurrentDownload = ByteFormat.Rate(RateFor(snapshot, rule.ProcessName, Direction.Inbound, elapsed));
            rule.CurrentUpload = ByteFormat.Rate(RateFor(snapshot, rule.ProcessName, Direction.Outbound, elapsed));
        }

        _lastTraffic = snapshot;
        _lastSampleTicks = now;
    }

    private double RateFor(IReadOnlyDictionary<string, long> snapshot, string process, Direction direction, double elapsed)
    {
        string key = PacketEngine.TrafficKey(process, direction);
        long current = snapshot.GetValueOrDefault(key);
        long previous = _lastTraffic.GetValueOrDefault(key);
        long delta = current - previous;
        return delta > 0 ? delta / elapsed : 0;
    }

    private async Task ApplyUpdateAsync()
    {
        if (_pendingUpdate is null) return;

        if (!_updates.CanSelfInstall || _pendingUpdate.SetupAssetUrl is null)
        {
            _updates.OpenReleasePage(_pendingUpdate);
            return;
        }

        try
        {
            IsBusy = true;
            UpdateText = "Downloading update…";
            var progress = new Progress<double>(p => UpdateText = $"Downloading update… {p:P0}");
            await _updates.DownloadAndLaunchAsync(_pendingUpdate, progress).ConfigureAwait(true);
            // The installer is now launching; ask the app to exit so files can be replaced.
            ShutdownRequested?.Invoke();
        }
        catch (Exception ex)
        {
            Notification?.Invoke($"Update failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SkipUpdate()
    {
        if (_pendingUpdate?.LatestVersion is { } v)
        {
            _settings.Current.SkippedVersion = v.ToString();
            _settings.Save();
        }
        IsUpdateAvailable = false;
    }

    /// <summary>Raised when an update download has launched the installer and the app should close.</summary>
    public event Action? ShutdownRequested;

    private void OnEngineFaulted(Exception ex)
    {
        // Marshalled to the UI thread via the dispatcher timer's thread affinity is
        // not guaranteed here, so post through the application dispatcher.
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            EngineEnabled = false;
            Persist();
            Notification?.Invoke($"The throttling engine stopped unexpectedly:\n\n{ex.Message}");
        });
    }

    public void Shutdown()
    {
        _statsTimer.Stop();
        _engine.Stop();
        Persist();
    }
}
