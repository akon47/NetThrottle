using System.ComponentModel;
using System.Threading;
using System.Windows;
using NetThrottle.App.Services;
using NetThrottle.App.ViewModels;
using Forms = System.Windows.Forms;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace NetThrottle.App;

/// <summary>
/// Application composition root: enforces a single instance, wires the services
/// and main view model by hand, shows the window, and keeps the app alive in the
/// notification tray (closing the window hides it; throttling keeps running).
/// </summary>
public partial class App : Application
{
    private const string InstanceMutexName = @"Global\NetThrottle.SingleInstance.{F1C6E2B8-7A4E-4C2E-9E4B-2B8D5C9A1F30}";

    private Mutex? _instanceMutex;
    private EngineController? _engine;
    private MainViewModel? _viewModel;
    private Forms.NotifyIcon? _tray;
    private bool _exiting;
    private bool _trayHintShown;

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

        // The window is hidden (not closed) when the user clicks X, so the app
        // must stay alive until we shut it down explicitly from the tray.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _engine = new EngineController();
        var settings = new SettingsService();
        var processes = new ProcessListProvider();
        var updates = new GitHubUpdateService();

        _viewModel = new MainViewModel(_engine, settings, processes, updates);

        var window = new MainWindow { DataContext = _viewModel };
        window.Closing += OnWindowClosing;
        _viewModel.Notification += message => window.Dispatcher.Invoke(() =>
            MessageBox.Show(window, message, "NetThrottle", MessageBoxButton.OK, MessageBoxImage.Information));
        _viewModel.ShutdownRequested += () => window.Dispatcher.Invoke(ExitApplication);
        MainWindow = window;

        CreateTrayIcon();
        window.Show();

        _ = _viewModel.CheckForUpdatesAsync(silent: true);
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open NetThrottle", null, (_, _) => ShowMainWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = "NetThrottle",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting || sender is not Window window) return;

        // Hide to tray instead of exiting.
        e.Cancel = true;
        window.Hide();

        if (!_trayHintShown)
        {
            _tray?.ShowBalloonTip(2500, "NetThrottle",
                "Still running in the tray — limits stay active. Right-click the icon to exit.",
                Forms.ToolTipIcon.Info);
            _trayHintShown = true;
        }
    }

    private void ShowMainWindow()
    {
        if (MainWindow is not { } window) return;
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
    }

    private void ExitApplication()
    {
        _exiting = true;
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Shutdown();
        _engine?.Dispose();
        _tray?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
