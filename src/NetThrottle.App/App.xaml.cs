using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using NetThrottle.App.Dialogs;
using NetThrottle.App.Localization;
using NetThrottle.App.Services;
using NetThrottle.App.ViewModels;

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
    private SettingsService? _settings;
    private GitHubUpdateService? _updates;
    private TaskbarIcon? _tray;
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
        _settings = new SettingsService();
        var settings = _settings;
        LocalizationService.Instance.Initialize(
            Path.Combine(AppContext.BaseDirectory, "locales"), settings.Current.Language);
        var processes = new ProcessListProvider();
        _updates = new GitHubUpdateService();

        _viewModel = new MainViewModel(_engine, settings, processes);

        var window = new MainWindow { DataContext = _viewModel };
        window.Closing += OnWindowClosing;
        _viewModel.Notification += message => window.Dispatcher.Invoke(() =>
            StyledMessageBox.Info(window, "NetThrottle", message));
        _viewModel.RequestUpdateCheck += () => window.Dispatcher.Invoke(() => _ = CheckForUpdatesAsync(manual: true));
        MainWindow = window;
        RestoreWindowPlacement(window, settings.Current);

        CreateTrayIcon();
        if (!settings.Current.StartMinimized)
            window.Show();

        _ = CheckForUpdatesAsync(manual: false);
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        var loc = LocalizationService.Instance;
        var owner = MainWindow;
        if (_updates is not { } updates) return;

        try
        {
            UpdateCheckResult result = await updates.CheckAsync();
            if (!result.IsUpdateAvailable)
            {
                if (manual) StyledMessageBox.Info(owner, loc["Update.Title"], loc["Update.None"]);
                return;
            }

            string version = "v" + (result.LatestVersion?.ToString(3) ?? result.Tag?.TrimStart('v', 'V') ?? string.Empty);

            // Portable build: no in-place install — offer the download page.
            if (!updates.CanSelfInstall || result.SetupAssetUrl is null)
            {
                if (StyledMessageBox.Confirm(owner, loc["Update.Title"], loc.Format("Update.PortableConfirmFormat", version)))
                    updates.OpenReleasePage(result);
                return;
            }

            if (!StyledMessageBox.Confirm(owner, loc["Update.Title"], loc.Format("Update.ConfirmFormat", version)))
                return;

            var progressDialog = new ProgressDialog(loc["Update.Title"], loc.Format("Update.DownloadingStatusFormat", version));
            if (owner is { IsVisible: true }) progressDialog.Owner = owner;
            else progressDialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            progressDialog.Show();

            try
            {
                var progress = new Progress<double>(p => progressDialog.Report(p * 100));
                string installer = await updates.DownloadAsync(result, progress, progressDialog.Token);
                if (progressDialog.IsVisible) progressDialog.Close();

                // Release the WinDivert driver (close its handle) so the installer
                // can replace WinDivert64.sys, then launch it and exit.
                _engine?.Stop();
                updates.RunInstaller(installer);
                ExitApplication();
            }
            catch (OperationCanceledException)
            {
                if (progressDialog.IsVisible) progressDialog.Close();
            }
            catch (Exception ex)
            {
                if (progressDialog.IsVisible) progressDialog.Close();
                StyledMessageBox.Error(owner, loc["Update.Title"], ex.Message);
            }
        }
        catch (Exception ex)
        {
            if (manual) StyledMessageBox.Error(owner, loc["Update.Title"], ex.Message);
        }
    }

    private static void RestoreWindowPlacement(Window window, NetThrottle.Core.Settings.AppSettings s)
    {
        if (s.WindowWidth is > 0 && s.WindowHeight is > 0 && s.WindowLeft is { } left && s.WindowTop is { } top)
        {
            double vsL = SystemParameters.VirtualScreenLeft, vsT = SystemParameters.VirtualScreenTop;
            double vsR = vsL + SystemParameters.VirtualScreenWidth, vsB = vsT + SystemParameters.VirtualScreenHeight;
            // Keep the window on a screen (guards against unplugged monitors).
            if (left + 80 < vsR && top + 40 < vsB && left + s.WindowWidth.Value > vsL && top + s.WindowHeight.Value > vsT)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = left;
                window.Top = top;
                window.Width = s.WindowWidth.Value;
                window.Height = s.WindowHeight.Value;
            }
        }
        if (s.WindowMaximized)
            window.WindowState = WindowState.Maximized;
    }

    private void SaveWindowPlacement(Window window)
    {
        if (_settings is null) return;
        var s = _settings.Current;
        var bounds = window.RestoreBounds; // normal-state bounds, even while maximized
        if (!bounds.IsEmpty)
        {
            s.WindowLeft = bounds.Left;
            s.WindowTop = bounds.Top;
            s.WindowWidth = bounds.Width;
            s.WindowHeight = bounds.Height;
        }
        s.WindowMaximized = window.WindowState == WindowState.Maximized;
        _settings.Save();
    }

    private void CreateTrayIcon()
    {
        var loc = LocalizationService.Instance;
        var theme = new ResourceDictionary { Source = new Uri("pack://application:,,,/Themes/DarkTheme.xaml") };

        // A WPF context menu, dark-styled from the shared theme (the menu lives
        // outside the window tree, so it carries its own theme dictionary).
        var menu = new ContextMenu { Style = theme[typeof(ContextMenu)] as Style };
        menu.Resources.MergedDictionaries.Add(theme);

        var open = new MenuItem { Header = loc["Tray.Open"] };
        open.Click += (_, _) => ShowMainWindow();
        var exit = new MenuItem { Header = loc["Tray.Exit"] };
        exit.Click += (_, _) => ExitApplication();
        menu.Items.Add(open);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);

        _tray = new TaskbarIcon
        {
            ToolTipText = "NetThrottle",
            ContextMenu = menu,
            Visibility = Visibility.Visible,
        };
        try
        {
            _tray.IconSource = BitmapFrame.Create(
                new Uri("pack://application:,,,/icon.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }
        catch { /* icon load failure is non-fatal */ }

        _tray.TrayLeftMouseUp += (_, _) => ShowMainWindow();
        _tray.TrayMouseDoubleClick += (_, _) => ShowMainWindow();
        _tray.ForceCreate();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting || sender is not Window window) return;

        // Hide to tray instead of exiting.
        e.Cancel = true;
        SaveWindowPlacement(window);
        window.Hide();

        if (!_trayHintShown)
        {
            try { _tray?.ShowNotification(title: "NetThrottle", message: LocalizationService.Instance["Tray.Hint"]); }
            catch { /* notifications can be disabled by the OS */ }
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
            _tray.Visibility = Visibility.Collapsed;
            _tray.Dispose();
            _tray = null;
        }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow is { } window) SaveWindowPlacement(window);
        _viewModel?.Shutdown();
        _engine?.Dispose();
        _tray?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
