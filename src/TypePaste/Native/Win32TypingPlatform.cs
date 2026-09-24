using System.Diagnostics;
using System.Runtime.InteropServices;
using TypePaste.Core.Hotkeys;
using TypePaste.Core.Input;
using TypePaste.Core.Typing;
using static TypePaste.Native.NativeMethods;

namespace TypePaste.Native;

/// <summary>
/// Windows implementation of <see cref="ITypingPlatform"/> based on <c>SendInput</c>. Create one per typing run on the
/// typing thread and dispose it afterwards.
/// </summary>
internal sealed unsafe class Win32TypingPlatform : ITypingPlatform, IDisposable
{
    /// <summary>Marks TypePaste's injected events in <c>dwExtraInfo</c> ("TPST").</summary>
    public const nint InjectionSignature = 0x54505354;

    private static readonly long MinWakeTicks = Stopwatch.Frequency * 3 / 1000; // 3 ms
    private static readonly long QuietTicks = Stopwatch.Frequency * 8 / 1000; // 8 ms
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    private readonly HotkeyGesture _startHotkey;
    private readonly HotkeyGesture _stopHotkey;
    private readonly bool _capsLockOn;
    private readonly ThreadStateProbe? _probe;
    private readonly PreciseWaiter _waiter = new();
    private INPUT[] _inputs = new INPUT[256];
    private uint? _lastContextSwitches;
    private int _idleMessageWait;
    private int _idleQuiet;
    private int _idleTimeouts;
    private long _idleWaitTicks;

    public Win32TypingPlatform(HotkeyGesture startHotkey, HotkeyGesture stopHotkey, bool capsLockOn, bool useIdleDetection)
    {
        _startHotkey = startHotkey;
        _stopHotkey = stopHotkey;
        _capsLockOn = capsLockOn;
        _probe = useIdleDetection ? ThreadStateProbe.TryCreate() : null;
    }

    public long TimestampFrequency => Stopwatch.Frequency;

    public long GetTimestamp() => Stopwatch.GetTimestamp();

    public void WaitUntil(long timestamp) => _waiter.WaitUntil(timestamp);

    public int SendKeyboardEvents(ReadOnlySpan<KeyboardEvent> events)
    {
        if (events.IsEmpty)
        {
            return 0;
        }

        if (_inputs.Length < events.Length)
        {
            _inputs = new INPUT[Math.Max(events.Length, _inputs.Length * 2)];
        }

        for (var i = 0; i < events.Length; i++)
        {
            var e = events[i];
            _inputs[i] = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = e.VirtualKey,
                        wScan = e.ScanCode,
                        dwFlags = (uint)e.Flags,
                        time = 0,
                        dwExtraInfo = InjectionSignature,
                    },
                },
            };
        }

        fixed (INPUT* inputs = _inputs)
        {
            return (int)SendInput((uint)events.Length, inputs, InputSize);
        }
    }

    public TargetState GetTargetState(TypingTarget target)
    {
        if (!IsAlive(target.Window))
        {
            return TargetState.Closed;
        }

        var foreground = GetForegroundWindow();
        if (foreground == 0)
        {
            // Activation changes briefly report no foreground window; look again before deciding.
            Thread.Sleep(5);
            foreground = GetForegroundWindow();
        }

        if (foreground == target.Window)
        {
            return TargetState.Active;
        }

        // A window that is being closed loses activation just before it is destroyed. Report that as "closed"
        // rather than as a focus change. Any other window becoming active (including a dialog of the target app,
        // such as "Save changes?") stops typing so keystrokes never land somewhere unexpected.
        Thread.Sleep(20);
        return IsAlive(target.Window) ? TargetState.FocusChanged : TargetState.Closed;
    }

    /// <summary>Human-readable pacing statistics of this run (for the diagnostics log).</summary>
    public string Diagnostics =>
        $"idle probe {(_probe is null ? "unavailable" : "active")}: {_idleMessageWait} message-wait, {_idleQuiet} quiet, " +
        $"{_idleTimeouts} timeouts, {_idleWaitTicks * 1000.0 / Stopwatch.Frequency:F0} ms waiting";

    private static bool IsAlive(nint window) => IsWindow(window) && IsWindowVisible(window);

    public bool IsStopKeyDown() => !_stopHotkey.IsEmpty && IsKeyDown(_stopHotkey.VirtualKey) && ModifiersDown(_stopHotkey.Modifiers);

    public bool AreBlockingKeysDown() =>
        IsKeyDown(VirtualKeys.Shift) || IsKeyDown(VirtualKeys.Control) || IsKeyDown(VirtualKeys.Menu) ||
        IsKeyDown(VirtualKeys.LeftWindows) || IsKeyDown(VirtualKeys.RightWindows) ||
        (!_startHotkey.IsEmpty && IsKeyDown(_startHotkey.VirtualKey));

    public bool IsCapsLockOn() => _capsLockOn;

    public IdleWaitResult WaitForTargetIdle(TypingTarget target, TimeSpan timeout)
    {
        if (_probe is null)
        {
            return IdleWaitResult.NotSupported;
        }

        var start = Stopwatch.GetTimestamp();
        var deadline = start + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        uint? previousSwitches = null;
        var lastActivity = start;
        try
        {
            while (true)
            {
                if (!_probe.TryQuery(target.ProcessId, target.ThreadId, out var snapshot))
                {
                    return IdleWaitResult.NotSupported;
                }

                var now = Stopwatch.GetTimestamp();
                if (previousSwitches != snapshot.ContextSwitches || !snapshot.IsWaiting)
                {
                    previousSwitches = snapshot.ContextSwitches;
                    lastActivity = now;
                }

                // 1) Classic Win32 apps: the input thread blocks in a message wait only once its queue is empty.
                //    Unless it has been scheduled since the last idle state, give the input a moment to arrive.
                var woke = _lastContextSwitches is null || snapshot.ContextSwitches != _lastContextSwitches;
                if (snapshot.IsWaitingForMessages && (woke || now - start >= MinWakeTicks))
                {
                    _idleMessageWait++;
                    _lastContextSwitches = snapshot.ContextSwitches;
                    return IdleWaitResult.Idle;
                }

                // 2) Apps that forward input elsewhere (Chromium, Electron, WebView2): their UI thread keeps waking up
                //    while the renderer works through the keystrokes. Once it has stayed asleep for a few milliseconds,
                //    the previous batch has been processed.
                if (now - lastActivity >= QuietTicks)
                {
                    _idleQuiet++;
                    _lastContextSwitches = snapshot.ContextSwitches;
                    return IdleWaitResult.Idle;
                }

                if (now >= deadline)
                {
                    _idleTimeouts++;
                    _lastContextSwitches = snapshot.ContextSwitches;
                    return IdleWaitResult.TimedOut;
                }

                Thread.Yield();
            }
        }
        finally
        {
            _idleWaitTicks += Stopwatch.GetTimestamp() - start;
        }
    }

    public IKeyboardLayoutMapper? CreateLayoutMapper(TypingTarget target)
    {
        var layout = GetKeyboardLayout((uint)target.ThreadId);
        return layout == 0 ? null : new Win32LayoutMapper(layout);
    }

    public void Dispose()
    {
        _probe?.Dispose();
        _waiter.Dispose();
    }

    private static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static bool ModifiersDown(HotkeyModifiers modifiers) =>
        (!modifiers.HasFlag(HotkeyModifiers.Control) || IsKeyDown(VirtualKeys.Control)) &&
        (!modifiers.HasFlag(HotkeyModifiers.Alt) || IsKeyDown(VirtualKeys.Menu)) &&
        (!modifiers.HasFlag(HotkeyModifiers.Shift) || IsKeyDown(VirtualKeys.Shift)) &&
        (!modifiers.HasFlag(HotkeyModifiers.Windows) || IsKeyDown(VirtualKeys.LeftWindows) || IsKeyDown(VirtualKeys.RightWindows));
}
