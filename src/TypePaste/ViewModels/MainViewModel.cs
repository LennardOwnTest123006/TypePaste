using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TypePaste.Core.Settings;
using TypePaste.Core.Text;
using TypePaste.Core.Typing;
using TypePaste.Native;
using TypePaste.Services;
using TypePaste.Views;

namespace TypePaste.ViewModels;

internal enum StatusKind
{
    Ready,
    Waiting,
    Typing,
    Completed,
    Stopped,
    Error,
}

/// <summary>
/// State and workflows of the main window: the editor statistics, the status display and the three ways to start
/// typing (global hotkey, Start button with countdown, and the Test pad).
/// </summary>
internal sealed class MainViewModel : ObservableObject
{
    private const int LargeTextThreshold = 200_000;
    private static readonly TimeSpan ResultOverlayDuration = TimeSpan.FromSeconds(2.2);

    private readonly AppHost _host;
    private readonly DispatcherTimer _statsTimer;
    private readonly DispatcherTimer _readyTimer;
    private TextStatistics _stats = TextStatistics.Empty;
    private StatusKind _status = StatusKind.Ready;
    private string _statusText = "Ready";
    private string _statusDetail = string.Empty;
    private double _progress;
    private bool _isBusy;
    private bool _isTyping;
    private bool _isSettingsOpen;
    private int _statsVersion;
    private TestPadWindow? _testPad;

    public MainViewModel(AppHost host)
    {
        _host = host;
        Settings = new SettingsViewModel(host, () => IsSettingsOpen = false);

        _statsTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
        _statsTimer.Tick += (_, _) =>
        {
            _statsTimer.Stop();
            _ = RefreshStatisticsAsync();
        };

        _readyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        _readyTimer.Tick += (_, _) =>
        {
            _readyTimer.Stop();
            if (!IsBusy)
            {
                SetReady();
            }
        };

        StartStopCommand = new RelayCommand(() =>
        {
            if (IsBusy)
            {
                _host.Typing.StopCurrent();
            }
            else
            {
                _ = StartWithCountdownAsync();
            }
        });
        TestCommand = new RelayCommand(() => _ = RunTestAsync(), () => !IsBusy);
        ClearCommand = new RelayCommand(() => ClearText(), () => Stats.Length > 0);
        PasteCommand = new RelayCommand(() => PasteText());
        OpenFileCommand = new RelayCommand(OpenFile);
        OpenSettingsCommand = new RelayCommand(() => IsSettingsOpen = true);
        CloseSettingsCommand = new RelayCommand(() => IsSettingsOpen = false);

        _host.SettingsChanged += (_, _) => OnSettingsChanged();
        _host.HotkeyStateChanged += (_, _) =>
        {
            RefreshHotkeyDisplay();
            if (!IsBusy)
            {
                SetReady();
            }
        };
        _host.Hotkeys.StartPressed += (_, _) => _ = OnStartHotkeyAsync();

        SetReady();
    }

    // ------------------------------------------------------------------ View hooks (set by MainWindow)

    public Func<string> GetText { get; set; } = () => string.Empty;

    public Action<string> ReplaceText { get; set; } = _ => { };

    public Action<string> InsertText { get; set; } = _ => { };

    public Func<Window?> GetWindow { get; set; } = () => null;

    // ------------------------------------------------------------------ Bindable state

    public SettingsViewModel Settings { get; }

    public ICommand StartStopCommand { get; }

    public ICommand TestCommand { get; }

    public ICommand ClearCommand { get; }

    public ICommand PasteCommand { get; }

    public ICommand OpenFileCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public ICommand CloseSettingsCommand { get; }

    public TextStatistics Stats
    {
        get => _stats;
        private set
        {
            if (SetProperty(ref _stats, value))
            {
                OnPropertyChanged(nameof(CharacterCountText));
                OnPropertyChanged(nameof(WordCountText));
                OnPropertyChanged(nameof(LineCountText));
                OnPropertyChanged(nameof(EstimateText));
                OnPropertyChanged(nameof(ControlCharacterWarning));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string CharacterCountText => Format.Count(Stats.Characters, "character", "characters");

    public string WordCountText => Format.Count(Stats.Words, "word", "words");

    public string LineCountText => Format.Count(Stats.Lines, "line", "lines");

    public string EstimateText
    {
        get
        {
            if (Stats.Length == 0)
            {
                return string.Empty;
            }

            var options = _host.Settings.ToTypingOptions();
            var estimate = options.EstimateDuration(Stats.Keystrokes, Stats.LineBreaks);
            return estimate is { } duration ? $"≈ {Format.Duration(duration)} to type" : "Types at maximum speed";
        }
    }

    public string ControlCharacterWarning => Stats.UntypeableControls > 0
        ? $"{Format.Count(Stats.UntypeableControls, "invisible control character", "invisible control characters")} will be skipped"
        : string.Empty;

    public StatusKind Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string StatusDetail
    {
        get => _statusDetail;
        private set => SetProperty(ref _statusDetail, value);
    }

    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    /// <summary>A countdown or typing session is active.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsTyping
    {
        get => _isTyping;
        private set => SetProperty(ref _isTyping, value);
    }

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => SetProperty(ref _isSettingsOpen, value);
    }

    public SpeedMode Speed
    {
        get => _host.Settings.Speed;
        set
        {
            if (_host.Settings.Speed != value)
            {
                _host.Settings.Speed = value;
                _host.NotifySettingsChanged();
            }
        }
    }

    public IReadOnlyList<string> StartHotkeyParts => _host.Settings.StartHotkey.Parts;

    public IReadOnlyList<string> StopHotkeyParts => _host.Settings.StopHotkey.Parts;

    public bool HotkeysPaused => _host.Settings.HotkeysPaused;

    public FontFamily EditorFontFamily => (FontFamily)Application.Current.FindResource(
        _host.Settings.EditorFont == EditorFontStyle.Monospace ? "MonoFont" : "UiFont");

    public string PlaceholderText =>
        $"Type or paste the text you want TypePaste to type for you.\n\nThen click into a text field in any app and press {_host.Settings.StartHotkey}.";

    // ------------------------------------------------------------------ Editor

    /// <summary>Called by the view whenever the editor text changes (statistics are refreshed shortly after).</summary>
    public void NotifyTextChanged()
    {
        _statsTimer.Stop();
        _statsTimer.Start();
        if (!IsBusy && Status is StatusKind.Completed or StatusKind.Stopped or StatusKind.Error)
        {
            SetReady();
        }
    }

    public void LoadFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > 20 * 1024 * 1024)
            {
                ShowError("File too large", "TypePaste can load text files up to 20 MB.");
                return;
            }

            ReplaceText(File.ReadAllText(path));
            SetStatus(StatusKind.Ready, "Ready", $"Loaded {info.Name}. Click into a text field and press {_host.Settings.StartHotkey}.");
        }
        catch (Exception ex)
        {
            App.Log.Warn($"Could not open file: {ex.Message}");
            ShowError("Could not open the file", ex.Message);
        }
    }

    private async Task RefreshStatisticsAsync()
    {
        var version = ++_statsVersion;
        var text = GetText();
        var stats = text.Length > LargeTextThreshold
            ? await Task.Run(() => TextStatistics.Compute(text))
            : TextStatistics.Compute(text);

        if (version == _statsVersion)
        {
            Stats = stats;
        }
    }

    private void ClearText()
    {
        ReplaceText(string.Empty);
        SetReady();
    }

    private void PasteText()
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                InsertText(Clipboard.GetText());
            }
            else
            {
                ShowError("Nothing to paste", "The clipboard does not contain text.");
            }
        }
        catch (Exception ex)
        {
            ShowError("Could not read the clipboard", ex.Message);
        }
    }

    private void OpenFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open a text file",
            Filter = "Text files|*.txt;*.md;*.csv;*.json;*.xml;*.html;*.htm;*.log;*.ini;*.yaml;*.yml;*.cs;*.js;*.ts;*.py|All files|*.*",
        };

        if (dialog.ShowDialog(GetWindow()) == true)
        {
            LoadFile(dialog.FileName);
        }
    }

    // ------------------------------------------------------------------ Typing workflows

    /// <summary>Global start hotkey: type into whatever text field is focused right now.</summary>
    private async Task OnStartHotkeyAsync()
    {
        if (_host.Typing.IsBusy)
        {
            // A second press while typing (or counting down) never starts a second run; the progress card already
            // shows how to stop.
            return;
        }

        var capture = TargetWindows.CaptureForeground();
        if (capture.Problem == TargetProblem.OwnWindow && _testPad is not null && capture.Target?.Window == _testPad.Handle)
        {
            await RunTestAsync();
            return;
        }

        var text = GetText();
        if (!CheckText(text, capture.Target?.Window ?? 0) || !CheckTarget(capture))
        {
            return;
        }

        using var session = _host.Typing.TryBeginSession(_host.Settings.StopHotkey);
        if (session is not null)
        {
            await TypeAsync(session, text, capture.Target!);
        }
    }

    /// <summary>Start button: count down (so the user can click into the target) and then type.</summary>
    private async Task StartWithCountdownAsync()
    {
        var text = GetText();
        if (!CheckText(text, 0))
        {
            return;
        }

        using var session = _host.Typing.TryBeginSession(_host.Settings.StopHotkey);
        if (session is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (_host.Settings.MinimizeOnStart && GetWindow() is { } window)
            {
                // Minimizing returns focus to the previously active app (and its text field).
                window.WindowState = WindowState.Minimized;
            }

            for (var seconds = _host.Settings.StartCountdownSeconds; seconds > 0; seconds--)
            {
                SetStatus(StatusKind.Waiting, $"Starting in {seconds}…", "Click into the text field where TypePaste should type.");
                _host.Overlay.ShowCountdown(seconds, $"Click into the target field · {_host.Settings.StopHotkey} to cancel", 0);
                try
                {
                    await Task.Delay(1000, session.Token);
                }
                catch (TaskCanceledException)
                {
                    SetStatus(StatusKind.Stopped, "Stopped", "Cancelled before typing started.");
                    _host.Overlay.ShowMessage(OverlayKind.Warning, "Cancelled", "Nothing was typed.", 0, ResultOverlayDuration);
                    return;
                }
            }

            var capture = TargetWindows.CaptureForeground();
            if (capture.Problem == TargetProblem.OwnWindow)
            {
                ShowError("TypePaste was still the active window",
                    "Click into the target text field during the countdown, or use the hotkey instead.", capture.Target?.Window ?? 0);
                return;
            }

            if (!CheckTarget(capture))
            {
                return;
            }

            await TypeAsync(session, text, capture.Target!);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Test typing: type into the built-in test pad and verify the result.</summary>
    private async Task RunTestAsync()
    {
        var text = GetText();
        if (!CheckText(text, 0))
        {
            return;
        }

        using var session = _host.Typing.TryBeginSession(_host.Settings.StopHotkey);
        if (session is null)
        {
            return;
        }

        if (_testPad is null)
        {
            _testPad = new TestPadWindow { Owner = GetWindow() };
            _testPad.Closed += (_, _) => _testPad = null;
            _testPad.RunAgainRequested += (_, _) => _ = RunTestAsync();
            _testPad.Show();
        }

        var pad = _testPad;
        pad.IsRunning = true;
        IsBusy = true;
        try
        {
            pad.PrepareForTyping();
            SetStatus(StatusKind.Waiting, "Preparing test…", "TypePaste is about to type into the test pad.");

            // Let the pad become the active window before the first keystroke.
            try
            {
                await Task.Delay(350, session.Token);
            }
            catch (TaskCanceledException)
            {
                SetStatus(StatusKind.Stopped, "Stopped", "Test cancelled.");
                return;
            }

            var capture = TargetWindows.Capture(pad.Handle);
            if (capture.Target is null)
            {
                ShowError("Test pad unavailable", "The test pad window could not be used.");
                return;
            }

            var result = await TypeAsync(session, text, capture.Target);
            if (_testPad == pad)
            {
                await pad.VerifyAsync(text, result);
            }
        }
        finally
        {
            IsBusy = false;
            if (_testPad == pad)
            {
                pad.IsRunning = false;
            }
        }
    }

    private async Task<TypingResult> TypeAsync(TypingSession session, string text, TypingTarget target)
    {
        var options = _host.Settings.ToTypingOptions();
        var stopName = _host.Settings.StopHotkey.ToString();
        IsBusy = true;
        IsTyping = true;
        Progress = 0;
        _readyTimer.Stop();
        SetStatus(StatusKind.Typing, "Typing…", $"Typing into {Shorten(target.DisplayName)} · {stopName} to stop");
        _host.Overlay.ShowTyping("Typing…", $"0% · {stopName} to stop", target.Window);
        _host.Tray.SetTooltip("TypePaste — typing…");
        App.Log.Info($"Typing {text.Length} code units into '{target.ProcessName}' ({options.Speed}, {options.Method}).");

        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            if (session.Progress is { } progress)
            {
                Progress = progress.Fraction;
                var percent = (int)(progress.Fraction * 100);
                StatusText = $"Typing… {percent}%";
                _host.Overlay.UpdateProgress(progress.Fraction, $"{percent}% · {stopName} to stop");
            }
        };
        timer.Start();

        TypingResult result;
        try
        {
            result = await session.TypeAsync(text, target, options, _host.Settings.StartHotkey, _host.Settings.StopHotkey);
        }
        finally
        {
            timer.Stop();
            IsTyping = false;
            IsBusy = false;
            _host.Tray.SetTooltip("TypePaste — ready");
        }

        Progress = result.TotalLength == 0 ? 1 : (double)result.TypedLength / result.TotalLength;
        App.Log.Info($"Typing finished: {result.Outcome}, {result.TypedLength}/{result.TotalLength} in {result.Elapsed.TotalMilliseconds:F0} ms.");
        ReportResult(result, text, target);
        return result;
    }

    private void ReportResult(TypingResult result, string text, TypingTarget target)
    {
        var typed = TextStatistics.CountCharacters(text, result.TypedLength);
        var total = Stats.Length == text.Length ? Stats.Characters : TextStatistics.CountCharacters(text, text.Length);
        var skipped = result.SkippedControlCharacters > 0
            ? $" · {Format.Count(result.SkippedControlCharacters, "control character", "control characters")} skipped"
            : string.Empty;

        switch (result.Outcome)
        {
            case TypingOutcome.Completed:
                var detail = $"Typed {Format.Count(typed, "character", "characters")} in {Format.Duration(result.Elapsed)}{skipped}.";
                SetStatus(StatusKind.Completed, "Completed", detail);
                _host.Overlay.ShowMessage(OverlayKind.Success, "Completed", detail, target.Window, ResultOverlayDuration);
                break;

            case TypingOutcome.Cancelled:
            case TypingOutcome.FocusLost:
            case TypingOutcome.TargetClosed:
                var reason = result.Outcome switch
                {
                    TypingOutcome.FocusLost => "Another window became active",
                    TypingOutcome.TargetClosed => "The target window was closed",
                    _ => "Stopped",
                };
                var stoppedDetail = $"{reason} after {typed.ToString("N0")} of {Format.Count(total, "character", "characters")}.";
                SetStatus(StatusKind.Stopped, "Stopped", stoppedDetail);
                _host.Overlay.ShowMessage(OverlayKind.Warning, "Stopped", stoppedDetail, target.Window, ResultOverlayDuration);
                break;

            case TypingOutcome.InputBlocked:
                ShowError("Windows blocked the keystrokes",
                    "The target may be running as administrator, or a protected screen (UAC, sign-in) is active. Start TypePaste as administrator to type into elevated apps.",
                    target.Window);
                break;

            case TypingOutcome.ModifierKeysHeld:
                ShowError("Typing did not start",
                    $"Release Shift, Ctrl, Alt, Win and {_host.Settings.StartHotkey} so they don't turn the text into shortcuts, then try again.",
                    target.Window);
                break;

            default:
                ShowError("Typing failed", result.ErrorMessage ?? "An unexpected error occurred.", target.Window);
                break;
        }

        _readyTimer.Start();
    }

    private bool CheckText(string text, nint anchor)
    {
        if (text.Length > 0)
        {
            return true;
        }

        ShowError("Nothing to type", "Type or paste your text into TypePaste first.", anchor, showWindow: true);
        return false;
    }

    private bool CheckTarget(TargetCapture capture)
    {
        var anchor = capture.Target?.Window ?? 0;
        switch (capture.Problem)
        {
            case TargetProblem.None:
                return true;
            case TargetProblem.OwnWindow:
                ShowError("Choose where to type", $"Click into a text field in another app, then press {_host.Settings.StartHotkey}. Use \"Test typing\" to try it here.", anchor);
                return false;
            case TargetProblem.DesktopOrTaskbar:
            case TargetProblem.NoActiveWindow:
                ShowError("No text field selected", $"Click into the text field where TypePaste should type, then press {_host.Settings.StartHotkey}.", anchor);
                return false;
            case TargetProblem.Elevated:
                ShowError("This app runs as administrator",
                    $"Windows does not allow {capture.ProcessName ?? "it"} to receive keystrokes from TypePaste. Start TypePaste as administrator to type into it.",
                    anchor);
                return false;
            default:
                return false;
        }
    }

    private void ShowError(string title, string detail, nint anchor = 0, bool showWindow = false)
    {
        SetStatus(StatusKind.Error, title, detail);

        // The overlay makes sure the message is seen even when TypePaste is hidden in the tray.
        _host.Overlay.ShowMessage(OverlayKind.Error, title, detail, anchor, TimeSpan.FromSeconds(4), force: true);
        if (showWindow && GetWindow() is { IsVisible: false })
        {
            App.ShowMainWindow();
        }

        _readyTimer.Stop();
        _readyTimer.Start();
    }

    private void SetStatus(StatusKind kind, string text, string detail)
    {
        Status = kind;
        StatusText = text;
        StatusDetail = detail;
    }

    private void SetReady()
    {
        Progress = 0;
        if (_host.Settings.HotkeysPaused)
        {
            SetStatus(StatusKind.Ready, "Hotkeys paused", "Resume hotkeys from the tray icon menu, or use the Start button.");
        }
        else if (_host.StartHotkeyError is { } error)
        {
            SetStatus(StatusKind.Error, "Hotkey unavailable", error);
        }
        else
        {
            SetStatus(StatusKind.Ready, "Ready", $"Click into any text field, then press {_host.Settings.StartHotkey} to type your text.");
        }
    }

    private void RefreshHotkeyDisplay()
    {
        OnPropertyChanged(nameof(StartHotkeyParts));
        OnPropertyChanged(nameof(StopHotkeyParts));
        OnPropertyChanged(nameof(HotkeysPaused));
        OnPropertyChanged(nameof(PlaceholderText));
    }

    private void OnSettingsChanged()
    {
        _host.Overlay.Enabled = _host.Settings.ShowOverlay;
        OnPropertyChanged(nameof(Speed));
        OnPropertyChanged(nameof(EstimateText));
        OnPropertyChanged(nameof(EditorFontFamily));
        RefreshHotkeyDisplay();
        if (!IsBusy && Status == StatusKind.Ready)
        {
            SetReady();
        }
    }

    private static string Shorten(string value) => value.Length > 48 ? value[..47] + "…" : value;
}
