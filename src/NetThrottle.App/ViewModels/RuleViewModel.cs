using NetThrottle.Core.Models;

namespace NetThrottle.App.ViewModels;

/// <summary>
/// Editable wrapper around a <see cref="ThrottleRule"/>. Limits are surfaced in
/// KB/s for the UI (0 = unlimited); live rates are pushed in by the host.
/// </summary>
public sealed class RuleViewModel : ViewModelBase
{
    public const double BytesPerKilobyte = 1024d;

    private string _currentDownload = "—";
    private string _currentUpload = "—";
    private bool _isRunning;

    public RuleViewModel(ThrottleRule model) => Model = model;

    public ThrottleRule Model { get; }

    /// <summary>Whether the process is currently running. Drives the red "dead" styling.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value);
    }

    /// <summary>True when this row actually constrains traffic in some direction.</summary>
    public bool HasLimit => Model.DownloadBytesPerSec > 0 || Model.UploadBytesPerSec > 0;

    /// <summary>Raised whenever an editable field changes, so the host can persist and re-apply.</summary>
    public event Action? Changed;

    public bool Enabled
    {
        get => Model.Enabled;
        set { if (Model.Enabled != value) { Model.Enabled = value; OnPropertyChanged(); RaiseChanged(); } }
    }

    public string ProcessName
    {
        get => Model.ProcessName;
        set
        {
            string v = value ?? string.Empty;
            if (!string.Equals(Model.ProcessName, v, StringComparison.Ordinal))
            {
                Model.ProcessName = v;
                OnPropertyChanged();
                RaiseChanged();
            }
        }
    }

    public ProtocolKind Protocol
    {
        get => Model.Protocol;
        set { if (Model.Protocol != value) { Model.Protocol = value; OnPropertyChanged(); RaiseChanged(); } }
    }

    /// <summary>Download cap in KB/s. 0 = unlimited.</summary>
    public double DownloadKBps
    {
        get => Model.DownloadBytesPerSec / BytesPerKilobyte;
        set
        {
            long bytes = (long)Math.Max(0, value * BytesPerKilobyte);
            if (Model.DownloadBytesPerSec != bytes) { Model.DownloadBytesPerSec = bytes; OnPropertyChanged(); OnPropertyChanged(nameof(HasLimit)); RaiseChanged(); }
        }
    }

    /// <summary>Upload cap in KB/s. 0 = unlimited.</summary>
    public double UploadKBps
    {
        get => Model.UploadBytesPerSec / BytesPerKilobyte;
        set
        {
            long bytes = (long)Math.Max(0, value * BytesPerKilobyte);
            if (Model.UploadBytesPerSec != bytes) { Model.UploadBytesPerSec = bytes; OnPropertyChanged(); OnPropertyChanged(nameof(HasLimit)); RaiseChanged(); }
        }
    }

    public string CurrentDownload
    {
        get => _currentDownload;
        set => SetProperty(ref _currentDownload, value);
    }

    public string CurrentUpload
    {
        get => _currentUpload;
        set => SetProperty(ref _currentUpload, value);
    }

    private void RaiseChanged() => Changed?.Invoke();
}
