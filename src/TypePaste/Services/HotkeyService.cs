using System.Runtime.InteropServices;
using TypePaste.Core.Hotkeys;
using static TypePaste.Native.NativeMethods;

namespace TypePaste.Services;

/// <summary>
/// Registers TypePaste's global hotkeys: the start hotkey is active whenever TypePaste runs (unless paused), the stop
/// hotkey only while typing so that Esc keeps working normally in every other situation.
/// </summary>
internal sealed class HotkeyService : IDisposable
{
    private const int StartId = 0x5401;
    private const int StopId = 0x5402;

    private readonly MessageWindow _window;
    private HotkeyGesture _start;
    private bool _startRegistered;
    private bool _stopRegistered;
    private int _suspendCount;

    public HotkeyService(MessageWindow window)
    {
        _window = window;
        _window.MessageReceived += OnMessage;
    }

    public event EventHandler? StartPressed;

    public event EventHandler? StopPressed;

    public HotkeyGesture StartGesture => _start;

    public bool IsStartRegistered => _startRegistered;

    /// <summary>Registers (or re-registers) the start hotkey. Returns an error message on failure.</summary>
    public string? SetStartHotkey(HotkeyGesture gesture)
    {
        UnregisterStart();
        _start = gesture;
        return _suspendCount == 0 ? RegisterStart() : null;
    }

    /// <summary>Temporarily removes the start hotkey (while a hotkey is being recorded or hotkeys are paused).</summary>
    public void Suspend()
    {
        if (_suspendCount++ == 0)
        {
            UnregisterStart();
        }
    }

    public string? Resume()
    {
        if (_suspendCount == 0)
        {
            return null;
        }

        return --_suspendCount == 0 ? RegisterStart() : null;
    }

    /// <summary>Tests whether a gesture could be registered (it is free) without keeping it.</summary>
    public string? Probe(HotkeyGesture gesture)
    {
        if (_startRegistered && gesture == _start)
        {
            return null;
        }

        const int probeId = 0x5403;
        if (!RegisterHotKey(_window.Handle, probeId, (uint)gesture.Modifiers | MOD_NOREPEAT, gesture.VirtualKey))
        {
            return Describe(gesture, Marshal.GetLastWin32Error());
        }

        UnregisterHotKey(_window.Handle, probeId);
        return null;
    }

    public bool EnableStopHotkey(HotkeyGesture gesture)
    {
        DisableStopHotkey();
        _stopRegistered = RegisterHotKey(_window.Handle, StopId, (uint)gesture.Modifiers | MOD_NOREPEAT, gesture.VirtualKey);
        if (!_stopRegistered)
        {
            // The typing engine also polls the physical key state, so stopping still works.
            App.Log.Warn($"Stop hotkey {gesture} could not be registered: {Describe(gesture, Marshal.GetLastWin32Error())}");
        }

        return _stopRegistered;
    }

    public void DisableStopHotkey()
    {
        if (_stopRegistered)
        {
            UnregisterHotKey(_window.Handle, StopId);
            _stopRegistered = false;
        }
    }

    public void Dispose()
    {
        _window.MessageReceived -= OnMessage;
        UnregisterStart();
        DisableStopHotkey();
    }

    private string? RegisterStart()
    {
        if (_start.IsEmpty || _startRegistered)
        {
            return null;
        }

        _startRegistered = RegisterHotKey(_window.Handle, StartId, (uint)_start.Modifiers | MOD_NOREPEAT, _start.VirtualKey);
        if (_startRegistered)
        {
            return null;
        }

        var error = Describe(_start, Marshal.GetLastWin32Error());
        App.Log.Warn(error);
        return error;
    }

    private void UnregisterStart()
    {
        if (_startRegistered)
        {
            UnregisterHotKey(_window.Handle, StartId);
            _startRegistered = false;
        }
    }

    private bool OnMessage(int msg, nint wParam, nint lParam)
    {
        if (msg != WM_HOTKEY)
        {
            return false;
        }

        switch ((int)wParam)
        {
            case StartId:
                StartPressed?.Invoke(this, EventArgs.Empty);
                return true;
            case StopId:
                StopPressed?.Invoke(this, EventArgs.Empty);
                return true;
            default:
                return false;
        }
    }

    private static string Describe(HotkeyGesture gesture, int error) => error == 1409
        ? $"{gesture} is already used by another application. Choose a different hotkey in Settings."
        : $"{gesture} could not be registered as a global hotkey ({new System.ComponentModel.Win32Exception(error).Message}).";
}
