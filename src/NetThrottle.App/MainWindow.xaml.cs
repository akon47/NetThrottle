using System.Windows;

namespace NetThrottle.App;

/// <summary>Interaction logic for MainWindow.xaml. All behavior lives in the view model.</summary>
public partial class MainWindow : Window
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
