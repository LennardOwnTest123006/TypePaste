using System.Windows;
using System.Windows.Threading;
using TypePaste.Core.Settings;
using TypePaste.Services;
using TypePaste.ViewModels;
using TypePaste.Views;

namespace TypePaste;

public partial class App : Application
{
    private readonly CommandLineOptions _options;
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private SplashScreen? _splash;
    private MainWindow? _mainWindow;
    private MainViewModel? _viewModel;
    private bool _trayHintShown;
    private bool _exiting;

    internal App(CommandLineOptions options, SettingsStore store, AppSettings settings, SplashScreen? splash)
    {
        _options = options;
        _store = store;
        _settings = settings;
        _splash = splash;
    }

    internal static Log Log { get; } = new();

    internal static AppHost? Host { get; private set; }

    /// <summary>Shows, restores and activates the main window.</summary>
    internal static void ShowMainWindow()
    {
        if (Current is App { _mainWindow: { } window })
        {
            if (!window.IsVisible)
            {
                window.Show();
            }

            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Activate();
            window.Topmost = true;
            window.Topmost = false;
            window.Focus();
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        Log.Info($"TypePaste starting (Windows {Environment.OSVersion.Version}, {(Environment.Is64BitProcess ? "x64" : "x86")}).");

        try
        {
            StartUp();
        }
        catch (Exception ex)
        {
            Log.Error("Startup failed", ex);
            _splash?.Close(TimeSpan.Zero);
            MessageBox.Show($"TypePaste could not start.\n\n{ex.Message}", "TypePaste", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("TypePaste exiting.");
        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        SaveState();
        base.OnSessionEnding(e);
    }

    private void StartUp()
    {
        var host = new AppHost(_store, _settings);
        Host = host;
        host.Theme.Apply(_settings.Theme);
        host.Overlay.Enabled = _settings.ShowOverlay;

        if (_settings.HotkeysPaused)
        {
            host.Hotkeys.Suspend();
        }

        host.ApplyStartHotkey();

        // The installer can also add the sign-in entry, so the registry is the source of truth. Re-writing it keeps
        // the path current after a reinstall to another folder.
        _settings.LaunchAtStartup = StartupRegistration.IsEnabled();
        if (_settings.LaunchAtStartup)
        {
            StartupRegistration.SetEnabled(true);
        }

        _viewModel = new MainViewModel(host);
        _mainWindow = new MainWindow(_viewModel);
        _mainWindow.ExitRequested += (_, _) => ExitApplication();
        _mainWindow.HiddenToTray += (_, _) => ShowTrayHint();
        _mainWindow.ContentRendered += (_, _) =>
        {
            _splash?.Close(TimeSpan.FromMilliseconds(250));
            _splash = null;
        };

        host.Messages.CloseRequested += (_, _) => ExitApplication();
        host.Messages.CommandLineReceived += (_, args) => HandleCommandLine(CommandLineOptions.Parse(args), activate: true);
        host.Tray.Activated += (_, _) => ShowMainWindow();
        host.Tray.MenuProvider = BuildTrayMenu;
        host.Tray.SetTooltip("TypePaste — ready");
        host.Tray.Show();

        if (_settings.RememberText && _store.LoadText() is { Length: > 0 } saved)
        {
            _viewModel.ReplaceText(saved);
        }

        var startHidden = _options.StartInTray || _settings.StartMinimizedToTray;
        if (!startHidden)
        {
            _mainWindow.Show();
        }
        else
        {
            // Create the window handle so the window can be shown instantly later.
            new System.Windows.Interop.WindowInteropHelper(_mainWindow).EnsureHandle();
            if (host.StartHotkeyError is null && !_settings.HotkeysPaused)
            {
                host.Tray.ShowNotification("TypePaste is ready", $"Press {_settings.StartHotkey} in any text field to type your text.");
            }
        }

        HandleCommandLine(_options, activate: false);

        if (host.StartHotkeyError is { } error)
        {
            host.Tray.ShowNotification("TypePaste hotkey unavailable", error);
        }
    }

    private void HandleCommandLine(CommandLineOptions options, bool activate)
    {
        if (options.LoadPath is { } path)
        {
            _viewModel?.LoadFile(path);
        }

        if (activate && !options.StartInTray)
        {
            ShowMainWindow();
        }
    }

    private IReadOnlyList<TrayMenuItem> BuildTrayMenu()
    {
        var host = Host!;
        var busy = host.Typing.IsBusy;
        return
        [
            new TrayMenuItem("Open TypePaste", ShowMainWindow, IsDefault: true),
            new TrayMenuItem("Stop typing", () => host.Typing.StopCurrent(), IsEnabled: busy),
            TrayMenuItem.Separator,
            new TrayMenuItem($"Pause hotkey ({host.Settings.StartHotkey})", () => host.SetHotkeysPaused(!host.Settings.HotkeysPaused), IsChecked: host.Settings.HotkeysPaused),
            new TrayMenuItem("Settings…", () =>
            {
                ShowMainWindow();
                if (_viewModel is not null)
                {
                    _viewModel.IsSettingsOpen = true;
                }
            }),
            TrayMenuItem.Separator,
            new TrayMenuItem("Exit TypePaste", ExitApplication),
        ];
    }

    private void ShowTrayHint()
    {
        if (_trayHintShown)
        {
            return;
        }

        _trayHintShown = true;
        Host?.Tray.ShowNotification("TypePaste is still running", $"{_settings.StartHotkey} keeps working. Open TypePaste from the tray icon, or right-click it to exit.");
    }

    private void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        Host?.Typing.StopCurrent();
        SaveState();
        _mainWindow?.CloseForExit();
        Host?.Dispose();
        Shutdown();
    }

    private void SaveState()
    {
        try
        {
            if (Host is not { } host)
            {
                return;
            }

            host.SaveNow();
            if (_settings.RememberText && _viewModel is not null)
            {
                _store.SaveText(_viewModel.GetText());
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not save state", ex);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;
        MessageBox.Show(
            $"Something went wrong, but TypePaste will keep running.\n\n{e.Exception.Message}\n\nDetails were written to {Log.FilePath}.",
            "TypePaste", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
