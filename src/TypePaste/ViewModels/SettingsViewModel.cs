using System.Diagnostics;
using System.Windows.Input;
using TypePaste.Core.Hotkeys;
using TypePaste.Core.Settings;
using TypePaste.Core.Typing;
using TypePaste.Services;
using InputMethod = TypePaste.Core.Typing.InputMethod;

namespace TypePaste.ViewModels;

/// <summary>Settings panel. Every change is applied immediately and saved automatically.</summary>
internal sealed class SettingsViewModel : ObservableObject
{
    private readonly AppHost _host;
    private string? _startHotkeyError;
    private string? _stopHotkeyError;

    public SettingsViewModel(AppHost host, Action close)
    {
        _host = host;
        CloseCommand = new RelayCommand(close);
        ResetHotkeysCommand = new RelayCommand(ResetHotkeys);
        OpenDataFolderCommand = new RelayCommand(OpenDataFolder);
        _host.SettingsChanged += (_, _) => OnPropertyChanged(string.Empty);
        _host.HotkeyStateChanged += (_, _) =>
        {
            if (_host.StartHotkeyError is { } error && !_host.Settings.HotkeysPaused)
            {
                StartHotkeyError = error;
            }
        };
        _startHotkeyError = _host.StartHotkeyError;
    }

    private AppSettings S => _host.Settings;

    public ICommand CloseCommand { get; }

    public ICommand ResetHotkeysCommand { get; }

    public ICommand OpenDataFolderCommand { get; }

    public string Version => _host.Version;

    public bool IsElevated => Native.TargetWindows.IsRunningElevated;

    // ------------------------------------------------------------------ Typing

    public SpeedMode Speed
    {
        get => S.Speed;
        set => Update(S.Speed != value, () => S.Speed = value);
    }

    public bool IsCustomSpeed => S.Speed == SpeedMode.Custom;

    public double CustomDelayMs
    {
        get => S.CustomDelayMs;
        set => Update(Math.Abs(S.CustomDelayMs - value) > 0.0001, () => S.CustomDelayMs = Math.Clamp(Math.Round(value, 1), 0, TypingOptions.MaxCustomDelayMs));
    }

    public string CustomDelayDescription
    {
        get
        {
            var delay = S.CustomDelayMs;
            return delay <= 0 ? "No delay (one keystroke at a time)" : $"≈ {1000 / delay:N0} characters per second";
        }
    }

    public int LineBreakDelayMs
    {
        get => S.LineBreakDelayMs;
        set => Update(S.LineBreakDelayMs != value, () => S.LineBreakDelayMs = Math.Clamp(value, 0, TypingOptions.MaxLineBreakDelayMs));
    }

    public LineBreakStyle LineBreak
    {
        get => S.LineBreak;
        set => Update(S.LineBreak != value, () => S.LineBreak = value);
    }

    public TabStyle Tab
    {
        get => S.Tab;
        set => Update(S.Tab != value, () => S.Tab = value);
    }

    public InputMethod InputMethod
    {
        get => S.InputMethod;
        set => Update(S.InputMethod != value, () => S.InputMethod = value);
    }

    public bool StopOnFocusChange
    {
        get => S.StopOnFocusChange;
        set => Update(S.StopOnFocusChange != value, () => S.StopOnFocusChange = value);
    }

    // ------------------------------------------------------------------ Hotkeys

    public HotkeyGesture StartHotkey => S.StartHotkey;

    public HotkeyGesture StopHotkey => S.StopHotkey;

    public string? StartHotkeyError
    {
        get => _startHotkeyError;
        private set => SetProperty(ref _startHotkeyError, value);
    }

    public string? StopHotkeyError
    {
        get => _stopHotkeyError;
        private set => SetProperty(ref _stopHotkeyError, value);
    }

    public int StartCountdownSeconds
    {
        get => S.StartCountdownSeconds;
        set => Update(S.StartCountdownSeconds != value, () => S.StartCountdownSeconds = Math.Clamp(value, 0, AppSettings.MaxCountdownSeconds));
    }

    public bool MinimizeOnStart
    {
        get => S.MinimizeOnStart;
        set => Update(S.MinimizeOnStart != value, () => S.MinimizeOnStart = value);
    }

    public bool HotkeysPaused
    {
        get => S.HotkeysPaused;
        set => _host.SetHotkeysPaused(value);
    }

    public void BeginHotkeyRecording() => _host.BeginHotkeyRecording();

    public void EndHotkeyRecording() => _host.EndHotkeyRecording();

    public void RecordStartHotkey(HotkeyGesture gesture)
    {
        if (!gesture.Validate(allowPlainEscape: false, out var error))
        {
            StartHotkeyError = error;
            return;
        }

        if (gesture == S.StopHotkey)
        {
            StartHotkeyError = "The start and stop hotkeys must be different.";
            return;
        }

        if (_host.Hotkeys.Probe(gesture) is { } unavailable)
        {
            StartHotkeyError = unavailable;
            return;
        }

        S.StartHotkey = gesture;
        StartHotkeyError = null;
        _host.ApplyStartHotkey();
        _host.NotifySettingsChanged();
    }

    public void RecordStopHotkey(HotkeyGesture gesture)
    {
        if (!gesture.Validate(allowPlainEscape: true, out var error))
        {
            StopHotkeyError = error;
            return;
        }

        if (gesture == S.StartHotkey)
        {
            StopHotkeyError = "The start and stop hotkeys must be different.";
            return;
        }

        S.StopHotkey = gesture;
        StopHotkeyError = null;
        _host.NotifySettingsChanged();
    }

    private void ResetHotkeys()
    {
        S.StartHotkey = HotkeyGesture.DefaultStart;
        S.StopHotkey = HotkeyGesture.DefaultStop;
        StartHotkeyError = null;
        StopHotkeyError = null;
        _host.ApplyStartHotkey();
        StartHotkeyError = _host.StartHotkeyError;
        _host.NotifySettingsChanged();
    }

    // ------------------------------------------------------------------ Appearance & behaviour

    public ThemePreference Theme
    {
        get => S.Theme;
        set => Update(S.Theme != value, () =>
        {
            S.Theme = value;
            _host.Theme.Apply(value);
        });
    }

    public EditorFontStyle EditorFont
    {
        get => S.EditorFont;
        set => Update(S.EditorFont != value, () => S.EditorFont = value);
    }

    public bool ShowOverlay
    {
        get => S.ShowOverlay;
        set => Update(S.ShowOverlay != value, () => S.ShowOverlay = value);
    }

    public bool RememberText
    {
        get => S.RememberText;
        set => Update(S.RememberText != value, () =>
        {
            S.RememberText = value;
            if (!value)
            {
                _host.Store.DeleteText();
            }
        });
    }

    public bool LaunchAtStartup
    {
        get => S.LaunchAtStartup;
        set => Update(S.LaunchAtStartup != value, () =>
        {
            if (StartupRegistration.SetEnabled(value))
            {
                S.LaunchAtStartup = value;
            }
        });
    }

    public bool StartMinimizedToTray
    {
        get => S.StartMinimizedToTray;
        set => Update(S.StartMinimizedToTray != value, () => S.StartMinimizedToTray = value);
    }

    public bool CloseToTray
    {
        get => S.CloseToTray;
        set => Update(S.CloseToTray != value, () => S.CloseToTray = value);
    }

    public string DataFolder => _host.Store.DataDirectory;

    private void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{DataFolder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.Log.Warn($"Could not open the data folder: {ex.Message}");
        }
    }

    private void Update(bool changed, Action apply)
    {
        if (!changed)
        {
            return;
        }

        apply();
        _host.NotifySettingsChanged();
    }
}
