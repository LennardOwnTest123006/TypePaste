using TypePaste.Core.Input;

namespace TypePaste.Core.Typing;

/// <summary>The window that receives the typed text, captured when typing is triggered.</summary>
/// <param name="Window">Top-level window handle that was active when typing started.</param>
/// <param name="ProcessId">Process that owns the window.</param>
/// <param name="ThreadId">GUI thread that receives the keyboard input (owner of the focused control).</param>
/// <param name="Title">Window title for display.</param>
/// <param name="ProcessName">Process name for display.</param>
public sealed record TypingTarget(nint Window, int ProcessId, int ThreadId, string? Title = null, string? ProcessName = null)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Title) ? ProcessName ?? "the active window" : Title!;
}

/// <summary>State of the target window while typing.</summary>
public enum TargetState
{
    Active,
    FocusChanged,
    Closed,
}

/// <summary>Result of waiting for the target application to process pending input.</summary>
public enum IdleWaitResult
{
    /// <summary>The target has processed its pending input and is waiting for more.</summary>
    Idle,

    /// <summary>The target did not become idle within the timeout.</summary>
    TimedOut,

    /// <summary>Idle detection is not available; the engine falls back to fixed pacing.</summary>
    NotSupported,
}

/// <summary>
/// Operating-system services the typing engine depends on. The Windows implementation uses
/// <c>SendInput</c>; tests use a fake so the engine logic can be verified on any platform.
/// </summary>
public interface ITypingPlatform
{
    /// <summary>Current high-resolution timestamp (same units as <see cref="System.Diagnostics.Stopwatch"/>).</summary>
    long GetTimestamp();

    /// <summary>Ticks per second of <see cref="GetTimestamp"/>.</summary>
    long TimestampFrequency { get; }

    /// <summary>Blocks precisely until <paramref name="timestamp"/> (returns immediately when it has passed).</summary>
    void WaitUntil(long timestamp);

    /// <summary>Injects keyboard events. Returns how many events were accepted by the system.</summary>
    int SendKeyboardEvents(ReadOnlySpan<KeyboardEvent> events);

    /// <summary>Whether the target window still exists and is still the active window.</summary>
    TargetState GetTargetState(TypingTarget target);

    /// <summary>Whether the physical stop key is currently held down (fallback in addition to the stop hotkey).</summary>
    bool IsStopKeyDown();

    /// <summary>
    /// Whether modifier keys (Shift/Ctrl/Alt/Win) or the start hotkey are still held. Typing waits until they are
    /// released so they cannot turn typed characters into shortcuts.
    /// </summary>
    bool AreBlockingKeysDown();

    /// <summary>Whether Caps Lock is currently on.</summary>
    bool IsCapsLockOn();

    /// <summary>Waits until the target's input thread has processed the input sent so far.</summary>
    IdleWaitResult WaitForTargetIdle(TypingTarget target, TimeSpan timeout);

    /// <summary>Creates a keyboard layout mapper for the target's input language, or null if unavailable.</summary>
    IKeyboardLayoutMapper? CreateLayoutMapper(TypingTarget target);
}
