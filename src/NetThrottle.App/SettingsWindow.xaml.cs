using System.Windows;
using NetThrottle.App.Controls;

namespace NetThrottle.App;

/// <summary>Settings dialog. Shares the main view model so its bindings (language,
/// future options) read and write the same state.</summary>
public partial class SettingsWindow : ThemedWindow
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
