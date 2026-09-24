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
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    private readonly HotkeyGesture _startHotkey;
    private readonly HotkeyGesture _stopHotkey;
    private readonly bool _capsLockOn;
    private readonly ThreadStateProbe? _probe;
    private readonly PreciseWaiter _waiter = new();
    private INPUT[] _inputs = new INPUT[256];
    private uint? _lastContextSwitches;

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
        if (!IsWindow(target.Window))
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

        return foreground != 0 && BelongsToTarget(foreground, target.Window) ? TargetState.Active : TargetState.FocusChanged;
    }

    /// <summary>True if <paramref name="window"/> is the target or a popup owned by the same top-level window.</summary>
    public static bool BelongsToTarget(nint window, nint target)
    {
        if (window == target)
        {
            return true;
        }

        var rootOwner = GetAncestor(window, GA_ROOTOWNER);
        return rootOwner != 0 && rootOwner == GetAncestor(target, GA_ROOTOWNER);
    }

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
        while (true)
        {
            if (!_probe.TryQuery(target.ProcessId, target.ThreadId, out var snapshot))
            {
                return IdleWaitResult.NotSupported;
            }

            var now = Stopwatch.GetTimestamp();

            // Idle = blocked in a message wait. Unless the thread has been scheduled since the last idle state
            // (context switch count changed), give the input a moment to arrive before trusting that.
            var woke = _lastContextSwitches is null || snapshot.ContextSwitches != _lastContextSwitches;
            if (snapshot.IsWaitingForMessages && (woke || now - start >= MinWakeTicks))
            {
                _lastContextSwitches = snapshot.ContextSwitches;
                return IdleWaitResult.Idle;
            }

            if (now >= deadline)
            {
                _lastContextSwitches = snapshot.ContextSwitches;
                return IdleWaitResult.TimedOut;
            }

            Thread.Yield();
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
