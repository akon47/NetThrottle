using System.Windows;
using NetThrottle.App.Controls;
using NetThrottle.App.Localization;

namespace NetThrottle.App.Dialogs;

public enum MessageKind { Info, Error, Question }

/// <summary>Dark, themed replacement for the system message box. Question shows
/// Yes/No (returns true on Yes); Info/Error show a single OK.</summary>
public partial class MessageDialog : ThemedWindow
{
    public MessageDialog(string title, string message, MessageKind kind)
    {
        InitializeComponent();

        var loc = LocalizationService.Instance;
        Title = title;
        MessageText.Text = message;

        bool question = kind == MessageKind.Question;
        PrimaryButton.Content = loc[question ? "Dialog.Yes" : "Dialog.Ok"];
        if (question)
        {
            SecondaryButton.Content = loc["Dialog.No"];
            SecondaryButton.Visibility = Visibility.Visible;
        }
    }

    private void Primary_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Secondary_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
