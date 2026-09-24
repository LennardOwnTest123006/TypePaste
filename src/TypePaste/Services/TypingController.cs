using System.Windows.Input;
using TypePaste.Core.Hotkeys;
using TypePaste.Core.Typing;
using TypePaste.Native;

namespace TypePaste.Services;

/// <summary>
/// Owns the single active typing session: guarantees that only one run happens at a time, enables the stop hotkey
/// for the duration of the session, and runs the engine on a dedicated high-priority thread so the UI stays responsive.
/// </summary>
internal sealed class TypingController
{
    private readonly HotkeyService _hotkeys;
    private TypingSession? _current;

    public TypingController(HotkeyService hotkeys)
    {
        _hotkeys = hotkeys;
        _hotkeys.StopPressed += (_, _) => StopCurrent();
    }

    public bool IsBusy => _current is not null;

    public TypingSession? Current => _current;

    /// <summary>Starts a session (countdown and/or typing). Returns null if one is already running.</summary>
    public TypingSession? TryBeginSession(HotkeyGesture stopHotkey)
    {
        if (_current is not null)
        {
            return null;
        }

        _current = new TypingSession(this);
        _hotkeys.EnableStopHotkey(stopHotkey);
        return _current;
    }

    public void StopCurrent() => _current?.Stop();

    internal void EndSession(TypingSession session)
    {
        if (!ReferenceEquals(_current, session))
        {
            return;
        }

        _hotkeys.DisableStopHotkey();
        _current = null;
    }
}

/// <summary>One countdown + typing run. Dispose (end) it when finished.</summary>
internal sealed class TypingSession : IDisposable
{
    private readonly TypingController _controller;
    private readonly CancellationTokenSource _cancellation = new();

    public TypingSession(TypingController controller)
    {
        _controller = controller;
    }

    public CancellationToken Token => _cancellation.Token;

    public TypingProgress? Progress { get; private set; }

    public TypingTarget? Target { get; private set; }

    public bool IsStopRequested => _cancellation.IsCancellationRequested;

    public void Stop()
    {
        try
        {
            _cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The session already ended.
        }
    }

    /// <summary>Types <paramref name="text"/> into <paramref name="target"/> on a background thread.</summary>
    public Task<TypingResult> TypeAsync(string text, TypingTarget target, TypingOptions options, HotkeyGesture startHotkey, HotkeyGesture stopHotkey)
    {
        Target = target;
        Progress = new TypingProgress(text.Length);
        var progress = Progress;
        var token = _cancellation.Token;
        var capsLock = Keyboard.IsKeyToggled(Key.CapsLock);
        var uiDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        var uiThreadId = (int)NativeMethods.GetCurrentThreadId();
        var completion = new TaskCompletionSource<TypingResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                using var platform = new Win32TypingPlatform(startHotkey, stopHotkey, capsLock, options.Speed == SpeedMode.Instant, uiDispatcher, uiThreadId);
                var result = new TypingEngine(platform).Run(text, target, options, progress, token);
                App.Log.Info($"Pacing: {platform.Diagnostics}.");
                completion.TrySetResult(result);
            }
            catch (Exception ex)
            {
                App.Log.Error("Typing thread failed", ex);
                completion.TrySetResult(new TypingResult(TypingOutcome.Failed, progress.TypedLength, text.Length, 0, TimeSpan.Zero, ex.Message));
            }
        })
        {
            IsBackground = true,
            Name = "TypePaste typing",
            Priority = ThreadPriority.AboveNormal,
        };

        thread.Start();
        return completion.Task;
    }

    public void Dispose()
    {
        _controller.EndSession(this);
        _cancellation.Dispose();
    }
}
