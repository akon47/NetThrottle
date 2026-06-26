using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using NetThrottle.App.Common;
using NetThrottle.App.Localization;
using NetThrottle.App.Services;
using NetThrottle.Core.Models;
using NetThrottle.Engine;

namespace NetThrottle.App.ViewModels;

/// <summary>
/// The window's view model. The grid shows every running process (name-sorted),
/// merged with any process that has a saved limit. Typing a cap applies it
/// immediately while the engine is on. A limited process that exits stays in the
/// list (shown red) and re-applies automatically when it runs again.
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly EngineController _engine;
    private readonly ISettingsService _settings;
    private readonly IProcessListProvider _processes;
    private readonly IUpdateService _updates;
    private readonly DispatcherTimer _statsTimer;
    private readonly DispatcherTimer _refreshTimer;
    private readonly Dictionary<string, RuleViewModel> _rowsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly LocalizationService _loc = LocalizationService.Instance;

    private IReadOnlyDictionary<string, long> _lastTraffic = new Dictionary<string, long>();
    private long _lastSampleTicks;
    private bool _engineEnabled;
    private string _filter = string.Empty;
    private bool _showOnlyLimited;
    private string _totalDownText = "0 B/s";
    private string _totalUpText = "0 B/s";
    private bool _isBusy;

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
        _loc.LanguageChanged += OnLanguageChanged;

        Entries = new ObservableCollection<RuleViewModel>();
        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.SortDescriptions.Add(new SortDescription(nameof(RuleViewModel.ProcessName), ListSortDirection.Ascending));
        EntriesView.Filter = MatchesFilter;

        // Seed rows from saved limits, then merge in the running processes.
        foreach (var rule in _settings.Current.Rules)
            AddRow(new RuleViewModel(rule) { IsRunning = false });
        RefreshProcesses();

        ToggleEngineCommand = new RelayCommand(() => ToggleEngine());
        RefreshProcessesCommand = new RelayCommand(RefreshProcesses);
        ClearLimitCommand = new RelayCommand(p => { if (p is RuleViewModel r) ClearLimit(r); });
        CheckUpdateCommand = new RelayCommand(() => _ = CheckForUpdatesAsync(silent: false));
        ApplyUpdateCommand = new RelayCommand(() => _ = ApplyUpdateAsync());
        SkipUpdateCommand = new RelayCommand(SkipUpdate);

        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statsTimer.Tick += (_, _) => SampleTraffic();
        _statsTimer.Start();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refreshTimer.Tick += (_, _) => RefreshProcesses();
        _refreshTimer.Start();

        // Restore the previous run's engine state. EngineEnabled starts false so
        // the setter fires and StatusText updates when the engine turns on.
        if (_settings.Current.EngineEnabled)
            ToggleEngine(forceOn: true);
    }

    public ObservableCollection<RuleViewModel> Entries { get; }
    public ICollectionView EntriesView { get; }
    public Array ProtocolOptions { get; } = Enum.GetValues(typeof(ProtocolKind));

    public bool IsPortable => SettingsPaths.IsPortable;
    public string ModeText => SettingsPaths.IsPortable ? "Portable" : "Installed";
    public string SettingsLocation => _settings.Path;
    public string AppVersion =>
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    public ICommand ToggleEngineCommand { get; }
    public ICommand RefreshProcessesCommand { get; }
    public ICommand ClearLimitCommand { get; }
    public ICommand CheckUpdateCommand { get; }
    public ICommand ApplyUpdateCommand { get; }
    public ICommand SkipUpdateCommand { get; }

    public event Action<string>? Notification;
    public event Action? ShutdownRequested;

    public bool EngineEnabled
    {
        get => _engineEnabled;
        set
        {
            if (SetProperty(ref _engineEnabled, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(ToggleButtonText));
            }
        }
    }

    public string StatusText => _loc[_engineEnabled ? "Status.Running" : "Status.Stopped"];

    public string ToggleButtonText =>
        (_engineEnabled ? "■  " : "▶  ") + _loc[_engineEnabled ? "Toolbar.Stop" : "Toolbar.Start"];

    public IReadOnlyList<LanguageOption> Languages => _loc.Languages;

    public LanguageOption? CurrentLanguage
    {
        get => _loc.Languages.FirstOrDefault(l => string.Equals(l.Code, _loc.CurrentCode, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is null) return;
            _loc.SetLanguage(value.Code);
            _settings.Current.Language = value.Code;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public string Filter
    {
        get => _filter;
        set { if (SetProperty(ref _filter, value ?? string.Empty)) EntriesView.Refresh(); }
    }

    /// <summary>When on, the grid shows only processes that carry a limit.</summary>
    public bool ShowOnlyLimited
    {
        get => _showOnlyLimited;
        set { if (SetProperty(ref _showOnlyLimited, value)) EntriesView.Refresh(); }
    }

    /// <summary>Combined live download rate across all processes.</summary>
    public string TotalDownText
    {
        get => _totalDownText;
        private set => SetProperty(ref _totalDownText, value);
    }

    /// <summary>Combined live upload rate across all processes.</summary>
    public string TotalUpText
    {
        get => _totalUpText;
        private set => SetProperty(ref _totalUpText, value);
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

    // --- Process list -------------------------------------------------------

    public void RefreshProcesses()
    {
        var running = _processes.GetRunningProcessNames();
        var runningSet = new HashSet<string>(running, StringComparer.OrdinalIgnoreCase);

        foreach (string name in running)
        {
            if (_rowsByName.TryGetValue(name, out var row))
                row.IsRunning = true;
            else
                AddRow(new RuleViewModel(new ThrottleRule { ProcessName = name, Protocol = ProtocolKind.Both }) { IsRunning = true });
        }

        // Processes that are no longer running: keep them only if they carry a
        // limit (shown red); otherwise drop them from the list.
        foreach (var row in _rowsByName.Values.ToList())
        {
            if (runningSet.Contains(row.ProcessName)) continue;
            if (row.HasLimit) row.IsRunning = false;
            else RemoveRow(row);
        }
    }

    private void AddRow(RuleViewModel row)
    {
        if (string.IsNullOrWhiteSpace(row.ProcessName) || _rowsByName.ContainsKey(row.ProcessName)) return;
        row.Changed += OnRuleChanged;
        _rowsByName[row.ProcessName] = row;
        Entries.Add(row);
    }

    private void RemoveRow(RuleViewModel row)
    {
        row.Changed -= OnRuleChanged;
        _rowsByName.Remove(row.ProcessName);
        Entries.Remove(row);
    }

    private bool MatchesFilter(object item)
    {
        if (item is not RuleViewModel row) return false;
        if (_showOnlyLimited && !row.HasLimit) return false;
        return string.IsNullOrWhiteSpace(_filter) ||
               row.ProcessName.Contains(_filter, StringComparison.OrdinalIgnoreCase);
    }

    private void ClearLimit(RuleViewModel row)
    {
        row.DownloadKBps = 0;
        row.UploadKBps = 0;
        // A cleared, already-dead row can now leave the list.
        if (!row.IsRunning) RemoveRow(row);
    }

    // --- Engine -------------------------------------------------------------

    private void ToggleEngine(bool forceOn = false)
    {
        bool turnOn = forceOn || !_engine.IsRunning;
        try
        {
            if (turnOn) { _engine.Start(ActiveModels()); EngineEnabled = true; }
            else { _engine.Stop(); EngineEnabled = false; }
            Persist();
        }
        catch (Exception ex)
        {
            EngineEnabled = false;
            Persist();
            Notification?.Invoke(_loc.Format("Msg.EngineFailed", ex.Message));
        }
    }

    private void OnRuleChanged()
    {
        Persist();
        if (_engine.IsRunning) _engine.ApplyRules(ActiveModels());
    }

    private IReadOnlyList<ThrottleRule> ActiveModels() =>
        Entries.Where(r => r.Enabled && r.HasLimit).Select(r => r.Model).ToList();

    private void Persist()
    {
        _settings.Current.Rules = Entries.Where(r => r.HasLimit).Select(r => r.Model).ToList();
        _settings.Current.EngineEnabled = EngineEnabled;
        _settings.Save();
    }

    // --- Live rates ---------------------------------------------------------

    private void SampleTraffic()
    {
        long now = Stopwatch.GetTimestamp();
        var snapshot = _engine.IsRunning ? _engine.SnapshotTraffic() : new Dictionary<string, long>();
        double elapsed = _lastSampleTicks == 0 ? 1 : (now - _lastSampleTicks) / (double)Stopwatch.Frequency;
        if (elapsed <= 0) elapsed = 1;

        foreach (var row in Entries)
        {
            if (!_engine.IsRunning || !row.IsRunning)
            {
                row.CurrentDownload = "—";
                row.CurrentUpload = "—";
                continue;
            }

            row.CurrentDownload = ByteFormat.Rate(RateFor(snapshot, row.ProcessName, Direction.Inbound, elapsed));
            row.CurrentUpload = ByteFormat.Rate(RateFor(snapshot, row.ProcessName, Direction.Outbound, elapsed));
        }

        UpdateTotals(snapshot, elapsed);

        _lastTraffic = snapshot;
        _lastSampleTicks = now;
    }

    private void UpdateTotals(IReadOnlyDictionary<string, long> snapshot, double elapsed)
    {
        long down = 0, up = 0;
        foreach (var (key, value) in snapshot)
        {
            long delta = value - _lastTraffic.GetValueOrDefault(key);
            if (delta <= 0) continue;
            if (key.EndsWith("|Inbound", StringComparison.Ordinal)) down += delta;
            else if (key.EndsWith("|Outbound", StringComparison.Ordinal)) up += delta;
        }

        TotalDownText = ByteFormat.Rate(_engine.IsRunning ? down / elapsed : 0);
        TotalUpText = ByteFormat.Rate(_engine.IsRunning ? up / elapsed : 0);
    }

    private double RateFor(IReadOnlyDictionary<string, long> snapshot, string process, Direction direction, double elapsed)
    {
        string key = PacketEngine.TrafficKey(process, direction);
        long delta = snapshot.GetValueOrDefault(key) - _lastTraffic.GetValueOrDefault(key);
        return delta > 0 ? delta / elapsed : 0;
    }

    // --- Updates ------------------------------------------------------------

    public async Task CheckForUpdatesAsync(bool silent)
    {
        try
        {
            UpdateCheckResult result = await _updates.CheckAsync().ConfigureAwait(true);

            if (!result.IsUpdateAvailable)
            {
                _pendingUpdate = null;
                IsUpdateAvailable = false;
                if (!silent) Notification?.Invoke(_loc.Format("Msg.LatestFormat", result.CurrentVersion.ToString(3)));
                return;
            }

            if (silent && string.Equals(_settings.Current.SkippedVersion, result.LatestVersion?.ToString(), StringComparison.Ordinal))
                return;

            _pendingUpdate = result;
            IsUpdateAvailable = true;
            UpdateText = _loc.Format("Update.AvailableFormat",
                result.LatestVersion?.ToString(3) ?? string.Empty, result.CurrentVersion.ToString(3));
        }
        catch (Exception ex)
        {
            if (!silent) Notification?.Invoke(_loc.Format("Msg.UpdateCheckFailedFormat", ex.Message));
        }
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
            UpdateText = _loc["Update.Downloading"];
            var progress = new Progress<double>(p => UpdateText = _loc.Format("Update.DownloadingFormat", p.ToString("P0")));
            await _updates.DownloadAndLaunchAsync(_pendingUpdate, progress).ConfigureAwait(true);
            ShutdownRequested?.Invoke();
        }
        catch (Exception ex)
        {
            Notification?.Invoke(_loc.Format("Msg.UpdateFailedFormat", ex.Message));
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

    private void OnEngineFaulted(Exception ex)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            EngineEnabled = false;
            Persist();
            Notification?.Invoke(_loc.Format("Msg.EngineStopped", ex.Message));
        });
    }

    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ToggleButtonText));
        OnPropertyChanged(nameof(CurrentLanguage));
        if (_pendingUpdate is { } u)
            UpdateText = _loc.Format("Update.AvailableFormat",
                u.LatestVersion?.ToString(3) ?? string.Empty, u.CurrentVersion.ToString(3));
    }

    public void Shutdown()
    {
        _statsTimer.Stop();
        _refreshTimer.Stop();
        _engine.Stop();
        Persist();
    }
}
