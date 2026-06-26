using System.ComponentModel;

namespace NetThrottle.App.Common;

/// <summary>
/// App-wide rate unit for the limit columns: KB/s (default) or MB/s. A singleton
/// so column headers can bind to <see cref="Label"/> and row view models can read
/// <see cref="BytesPerUnit"/> when converting their cap values.
/// </summary>
public sealed class DisplayUnits : INotifyPropertyChanged
{
    public static DisplayUnits Instance { get; } = new();

    private bool _useMegabytes;

    private DisplayUnits() { }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Changed;

    public bool UseMegabytes
    {
        get => _useMegabytes;
        set
        {
            if (_useMegabytes == value) return;
            _useMegabytes = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UseMegabytes)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BytesPerUnit)));
            Changed?.Invoke();
        }
    }

    public double BytesPerUnit => _useMegabytes ? 1024d * 1024d : 1024d;

    public string Label => _useMegabytes ? "MB/s" : "KB/s";
}
