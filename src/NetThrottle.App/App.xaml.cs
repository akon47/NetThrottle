using System.Threading;
using System.Windows;
using NetThrottle.App.Services;
using NetThrottle.App.ViewModels;

namespace NetThrottle.App;

/// <summary>
/// Application composition root: enforces a single instance, wires the services
/// and main view model by hand, and shows the window.
/// </summary>
public partial class App : Application
{
    private const string InstanceMutexName = @"Global\NetThrottle.SingleInstance.{F1C6E2B8-7A4E-4C2E-9E4B-2B8D5C9A1F30}";

    private Mutex? _instanceMutex;
    private EngineController? _engine;
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(true, InstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("NetThrottle is already running.", "NetThrottle",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _engine = new EngineController();
        var settings = new SettingsService();
        var processes = new ProcessListProvider();
        var updates = new GitHubUpdateService();

        _viewModel = new MainViewModel(_engine, settings, processes, updates);

        var window = new MainWindow { DataContext = _viewModel };
        _viewModel.Notification += message => window.Dispatcher.Invoke(() =>
            MessageBox.Show(window, message, "NetThrottle", MessageBoxButton.OK, MessageBoxImage.Information));
        _viewModel.ShutdownRequested += () => window.Dispatcher.Invoke(Shutdown);
        MainWindow = window;
        window.Show();

        // Quiet update check on launch: installed builds prompt, portable builds link out.
        _ = _viewModel.CheckForUpdatesAsync(silent: true);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Shutdown();
        _engine?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
