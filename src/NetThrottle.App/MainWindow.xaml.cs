using System.Windows;
using NetThrottle.App.Controls;

namespace NetThrottle.App;

/// <summary>Interaction logic for MainWindow.xaml. All behavior lives in the view model.</summary>
public partial class MainWindow : ThemedWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        new SettingsWindow { Owner = this, DataContext = DataContext }.ShowDialog();
    }
}
