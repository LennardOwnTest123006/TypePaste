using System.Windows.Threading;
using TypePaste.Core.Settings;
using TypePaste.Views;

namespace TypePaste.Services;

/// <summary>
/// Owns the long-lived services of the running app and the current settings. Created once on the UI thread.
/// </summary>
internal sealed class AppHost : IDisposable
{
    private readonly DispatcherTimer _saveTimer;
    private int _hotkeyPauseDepth;

    public AppHost(SettingsStore store, AppSettings settings)
    {
        Store = store;
        Settings = settings;
        Theme = new ThemeService();
        Messages = new MessageWindow();
        Hotkeys = new HotkeyService(Messages);
        Typing = new TypingController(Hotkeys);
        Tray = new TrayIcon(Messages, "TypePaste");
        Overlay = new OverlayPresenter();

        Messages.SettingChanged += (_, area) => Theme.OnSystemSettingChanged(area);

        _saveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) => SaveNow();
    }

    public SettingsStore Store { get; }

    public AppSettings Settings { get; }

    public ThemeService Theme { get; }

    public MessageWindow Messages { get; }

    public HotkeyService Hotkeys { get; }

    public TypingController Typing { get; }

    public TrayIcon Tray { get; }

    public OverlayPresenter Overlay { get; }

    /// <summary>Error from the last attempt to register the start hotkey, or null when it is active.</summary>
    public string? StartHotkeyError { get; private set; }

    /// <summary>Raised after any setting changed (view-models refresh their bindings).</summary>
    public event EventHandler? SettingsChanged;

    /// <summary>Raised when the start hotkey registration state changed.</summary>
    public event EventHandler? HotkeyStateChanged;

    public string Version { get; } = typeof(AppHost).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";

    public void NotifySettingsChanged()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        Store.Save(Settings);
    }

    /// <summary>Registers the configured start hotkey (unless hotkeys are paused).</summary>
    public void ApplyStartHotkey()
    {
        // While paused the hotkey is only remembered; it is registered when hotkeys are resumed.
        var error = Hotkeys.SetStartHotkey(Settings.StartHotkey);
        StartHotkeyError = Settings.HotkeysPaused ? null : error;
        HotkeyStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetHotkeysPaused(bool paused)
    {
        if (Settings.HotkeysPaused == paused)
        {
            return;
        }

        Settings.HotkeysPaused = paused;
        if (paused)
        {
            Hotkeys.Suspend();
            StartHotkeyError = null;
        }
        else
        {
            StartHotkeyError = Hotkeys.Resume();
        }

        HotkeyStateChanged?.Invoke(this, EventArgs.Empty);
        NotifySettingsChanged();
    }

    /// <summary>Temporarily disables the start hotkey (while recording a new one).</summary>
    public void BeginHotkeyRecording()
    {
        if (_hotkeyPauseDepth++ == 0)
        {
            Hotkeys.Suspend();
        }
    }

    public void EndHotkeyRecording()
    {
        if (_hotkeyPauseDepth > 0 && --_hotkeyPauseDepth == 0)
        {
            var error = Hotkeys.Resume();
            if (!Settings.HotkeysPaused)
            {
                StartHotkeyError = error;
                HotkeyStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public void Dispose()
    {
        _saveTimer.Stop();
        Overlay.Dispose();
        Tray.Dispose();
        Hotkeys.Dispose();
        Messages.Dispose();
    }
}
