using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Windows;
using NetThrottle.App.Localization;
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
        LocalizationService.Instance.Initialize(
            Path.Combine(AppContext.BaseDirectory, "locales"), settings.Current.Language);
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
        var loc = LocalizationService.Instance;
        var menu = new Forms.ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            BackColor = System.Drawing.Color.FromArgb(0x33, 0x33, 0x37),
            ForeColor = System.Drawing.Color.White,
            ShowImageMargin = false,
        };
        menu.Items.Add(loc["Tray.Open"], null, (_, _) => ShowMainWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(loc["Tray.Exit"], null, (_, _) => ExitApplication());

        _tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = "NetThrottle",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ShowMainWindow();
    }

    /// <summary>Dark renderer/colors for the WinForms tray menu so it matches the app theme.</summary>
    private sealed class DarkMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = System.Drawing.Color.White;
            base.OnRenderItemText(e);
        }
    }

    private sealed class DarkColorTable : Forms.ProfessionalColorTable
    {
        private static System.Drawing.Color Rgb(int r, int g, int b) => System.Drawing.Color.FromArgb(r, g, b);

        public override System.Drawing.Color ToolStripDropDownBackground => Rgb(0x33, 0x33, 0x37);
        public override System.Drawing.Color ImageMarginGradientBegin => Rgb(0x33, 0x33, 0x37);
        public override System.Drawing.Color ImageMarginGradientMiddle => Rgb(0x33, 0x33, 0x37);
        public override System.Drawing.Color ImageMarginGradientEnd => Rgb(0x33, 0x33, 0x37);
        public override System.Drawing.Color MenuBorder => Rgb(0x3F, 0x3F, 0x46);
        public override System.Drawing.Color MenuItemBorder => Rgb(0x0A, 0x84, 0xFF);
        public override System.Drawing.Color MenuItemSelected => Rgb(0x2A, 0x3A, 0x55);
        public override System.Drawing.Color MenuItemSelectedGradientBegin => Rgb(0x2A, 0x3A, 0x55);
        public override System.Drawing.Color MenuItemSelectedGradientEnd => Rgb(0x2A, 0x3A, 0x55);
        public override System.Drawing.Color SeparatorDark => Rgb(0x3F, 0x3F, 0x46);
        public override System.Drawing.Color SeparatorLight => Rgb(0x3F, 0x3F, 0x46);
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
                LocalizationService.Instance["Tray.Hint"], Forms.ToolTipIcon.Info);
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
