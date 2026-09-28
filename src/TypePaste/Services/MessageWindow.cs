using System.Runtime.InteropServices;
using TypePaste.Native;
using static TypePaste.Native.NativeMethods;

namespace TypePaste.Services;

/// <summary>
/// Hidden top-level Win32 window that receives global hotkeys, tray icon callbacks, taskbar restarts and messages
/// from other TypePaste processes. It lives on the UI thread; the WPF dispatcher pumps its messages. The window
/// class name is used by the installer and by second instances to find the running app.
/// </summary>
internal sealed class MessageWindow : IDisposable
{
    public const string ClassName = "TypePaste.MessageWindow";

    private static readonly WndProc Procedure = StaticWndProc;
    private static readonly Dictionary<nint, MessageWindow> Instances = new();
    private static bool _classRegistered;

    private readonly uint _taskbarCreatedMessage;

    public MessageWindow()
    {
        EnsureClassRegistered();
        _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
        Handle = CreateWindowExW(WS_EX_TOOLWINDOW, ClassName, "TypePaste", WS_POPUP, 0, 0, 0, 0, 0, 0, GetModuleHandleW(null), 0);
        if (Handle == 0)
        {
            throw new InvalidOperationException($"Could not create the message window: {DescribeLastError()}");
        }

        Instances[Handle] = this;

        // If TypePaste runs as administrator, still accept the command line from normal instances and the
        // taskbar-restart broadcast from Explorer.
        ChangeWindowMessageFilterEx(Handle, WM_COPYDATA, MSGFLT_ALLOW, 0);
        if (_taskbarCreatedMessage != 0)
        {
            ChangeWindowMessageFilterEx(Handle, _taskbarCreatedMessage, MSGFLT_ALLOW, 0);
        }
    }

    /// <summary>Raised for every message; return true from a handler to mark it handled.</summary>
    public event Func<int, nint, nint, bool>? MessageReceived;

    /// <summary>Another process asked TypePaste to close (installer/uninstaller or Windows shutting down).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>A second TypePaste instance forwarded its command line.</summary>
    public event EventHandler<string[]>? CommandLineReceived;

    /// <summary>Explorer restarted; tray icons must be added again.</summary>
    public event EventHandler? TaskbarCreated;

    /// <summary>A system setting changed (for example light/dark mode); the argument is the setting area.</summary>
    public event EventHandler<string?>? SettingChanged;

    public nint Handle { get; private set; }

    public void Dispose()
    {
        if (Handle != 0)
        {
            Instances.Remove(Handle);
            DestroyWindow(Handle);
            Handle = 0;
        }
    }

    private static void EnsureClassRegistered()
    {
        if (_classRegistered)
        {
            return;
        }

        var windowClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Procedure),
            hInstance = GetModuleHandleW(null),
            lpszClassName = ClassName,
        };

        if (RegisterClassExW(ref windowClass) == 0)
        {
            throw new InvalidOperationException($"Could not register the message window class: {DescribeLastError()}");
        }

        _classRegistered = true;
    }

    private static nint StaticWndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            if (Instances.TryGetValue(hwnd, out var window) && window.HandleMessage((int)msg, wParam, lParam) is { } result)
            {
                return result;
            }
        }
        catch (Exception ex)
        {
            App.Log.Error("Message window handler failed", ex);
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private nint? HandleMessage(int msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_CLOSE:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return 0;

            case WM_QUERYENDSESSION:
                return 1;

            case WM_ENDSESSION when wParam != 0:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return 0;

            case WM_COPYDATA:
                HandleCopyData(lParam);
                return 1;

            case WM_SETTINGCHANGE:
                SettingChanged?.Invoke(this, lParam != 0 ? Marshal.PtrToStringUni(lParam) : null);
                break;
        }

        if (_taskbarCreatedMessage != 0 && msg == (int)_taskbarCreatedMessage)
        {
            TaskbarCreated?.Invoke(this, EventArgs.Empty);
            return 0;
        }

        return MessageReceived?.Invoke(msg, wParam, lParam) == true ? 0 : null;
    }

    private void HandleCopyData(nint lParam)
    {
        var data = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
        if (data.dwData != SingleInstance.CopyDataSignature || data.cbData <= 0 || data.lpData == 0)
        {
            return;
        }

        var payload = Marshal.PtrToStringUni(data.lpData, data.cbData / 2);
        var args = payload.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        CommandLineReceived?.Invoke(this, args);
    }
}
