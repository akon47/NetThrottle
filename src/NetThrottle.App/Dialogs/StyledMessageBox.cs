using System.Linq;
using System.Windows;

namespace NetThrottle.App.Dialogs;

/// <summary>Themed replacements for <c>MessageBox.Show</c>.</summary>
public static class StyledMessageBox
{
    public static void Info(Window? owner, string title, string message) =>
        Show(owner, title, message, MessageKind.Info);

    public static void Error(Window? owner, string title, string message) =>
        Show(owner, title, message, MessageKind.Error);

    /// <summary>Yes/No prompt. Returns true when the user confirms.</summary>
    public static bool Confirm(Window? owner, string title, string message) =>
        Show(owner, title, message, MessageKind.Question);

    private static bool Show(Window? owner, string title, string message, MessageKind kind)
    {
        var dialog = new MessageDialog(title, message, kind);
        SetOwner(dialog, owner);
        return dialog.ShowDialog() == true;
    }

    private static void SetOwner(Window dialog, Window? owner)
    {
        owner ??= Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w != dialog)
                  ?? Application.Current?.MainWindow;

        if (owner is { IsVisible: true } && owner != dialog)
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }
}
