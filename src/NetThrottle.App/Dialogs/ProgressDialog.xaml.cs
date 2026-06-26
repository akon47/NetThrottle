using System.Globalization;
using System.Threading;
using System.Windows;
using NetThrottle.App.Controls;
using NetThrottle.App.Localization;

namespace NetThrottle.App.Dialogs;

/// <summary>Modeless themed progress dialog with a cancel button, used for the
/// update download. Cancelling signals <see cref="Token"/> and closes the dialog.</summary>
public partial class ProgressDialog : ThemedWindow
{
    private readonly CancellationTokenSource _cts = new();

    public ProgressDialog(string title, string status)
    {
        InitializeComponent();
        Title = title;
        StatusText.Text = status;
        CancelButton.Content = LocalizationService.Instance["Dialog.Cancel"];
    }

    public CancellationToken Token => _cts.Token;

    /// <summary>Sets the progress (0–100). Call on the UI thread.</summary>
    public void Report(double percent)
    {
        Bar.Value = percent;
        PercentText.Text = percent.ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        Close();
    }
}
