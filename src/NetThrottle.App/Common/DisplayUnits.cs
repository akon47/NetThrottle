using System.ComponentModel;

namespace NetThrottle.App.Common;

/// <summary>
/// App-wide rate unit for the limit columns: Kbps (default) or Mbps. A singleton
/// so column headers can bind to <see cref="Label"/> and row view models can read
/// <see cref="BytesPerUnit"/> when converting their cap values. Network units are
/// bits, decimal: 1 Kbps = 1000 bits/s = 125 bytes/s.
/// </summary>
public sealed class DisplayUnits : INotifyPropertyChanged
{
    public static DisplayUnits Instance { get; } = new();

    private bool _useMegabits;

    private DisplayUnits() { }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Changed;

    public bool UseMegabits
    {
        get => _useMegabits;
        set
        {
            if (_useMegabits == value) return;
            _useMegabits = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UseMegabits)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BytesPerUnit)));
            Changed?.Invoke();
        }
    }

    /// <summary>Bytes/sec per one display unit: 125 for Kbps, 125000 for Mbps.</summary>
    public double BytesPerUnit => _useMegabits ? 125_000d : 125d;

    public string Label => _useMegabits ? "Mbps" : "Kbps";
}
