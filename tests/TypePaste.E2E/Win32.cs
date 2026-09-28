using System.Runtime.InteropServices;

namespace TypePaste.E2E;

/// <summary>Win32 helpers used to drive windows like a real user would (keyboard, mouse, window queries).</summary>
internal static unsafe class Win32
{
    public const ushort VK_BACK = 0x08;
    public const ushort VK_RETURN = 0x0D;
    public const ushort VK_SHIFT = 0x10;
    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_MENU = 0x12;
    public const ushort VK_ESCAPE = 0x1B;
    public const ushort VK_DELETE = 0x2E;
    public const ushort VK_A = 0x41;
    public const ushort VK_F6 = 0x75;
    public const ushort VK_F9 = 0x78;
    public const ushort VK_LSHIFT = 0xA0;
    public const ushort VK_LCONTROL = 0xA2;

    public const int WM_CLOSE = 0x0010;
    public const int WM_COMMAND = 0x0111;
    public const int WM_SETTEXT = 0x000C;
    public const int WM_GETTEXT = 0x000D;
    public const int WM_GETTEXTLENGTH = 0x000E;
    public const int WM_SYSCOMMAND = 0x0112;
    public const int SC_MINIMIZE = 0xF020;
    public const int SW_MINIMIZE = 6;
    public const int SW_RESTORE = 9;
    public const int SW_SHOW = 5;

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x1;
    private const uint KEYEVENTF_KEYUP = 0x2;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x2;
    private const uint MOUSEEVENTF_LEFTUP = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;

        public override readonly string ToString() => $"({Left},{Top})-({Right},{Bottom})";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    public delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT* pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(nint hWnd, out RECT rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint FindWindowW(string? className, string? windowName);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out int processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint hWnd, char* text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint hWnd, char* text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool PostMessageW(nint hWnd, int msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeoutW(nint hWnd, int msg, nint wParam, nint lParam, uint flags, uint timeout, out nint result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeoutW(nint hWnd, int msg, nint wParam, char* lParam, uint flags, uint timeout, out nint result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeoutW(nint hWnd, int msg, nint wParam, string lParam, uint flags, uint timeout, out nint result);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vk);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    public static extern nint WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    public static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    public static readonly nint HWND_TOPMOST = -1;
    public const uint SWP_NOSIZE = 0x1;
    public const uint SWP_NOMOVE = 0x2;
    public const uint SWP_SHOWWINDOW = 0x40;

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONIDENTIFIER
    {
        public int cbSize;
        public nint hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    /// <summary>Returns the screen rectangle of a notification-area icon, or null if it does not exist.</summary>
    public static RECT? NotifyIconRect(nint owner, uint id)
    {
        var identifier = new NOTIFYICONIDENTIFIER { cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(), hWnd = owner, uID = id };
        return Shell_NotifyIconGetRect(ref identifier, out var rect) == 0 ? rect : null;
    }

    public const int WM_GETICON = 0x007F;

    /// <summary>Returns the big or small window icon handle (0 if none).</summary>
    public static nint GetWindowIcon(nint hwnd, bool big)
    {
        SendMessageTimeoutW(hwnd, WM_GETICON, big ? 1 : 0, 0, 0, 2000, out var icon);
        return icon;
    }

    /// <summary>The top-level window that would receive a click at the given screen point.</summary>
    public static nint TopLevelAt(int x, int y) => GetAncestor(WindowFromPoint(new POINT { X = x, Y = y }), 2 /* GA_ROOT */);

    /// <summary>
    /// Minimizes console windows (such as the runner agent's console) so that test clicks can never land in them;
    /// a click would start a QuickEdit selection there.
    /// </summary>
    public static int MinimizeConsoleWindows()
    {
        var consoles = TopLevelWindows(h => IsWindowVisible(h) && GetClassName(h) is "ConsoleWindowClass" or "CASCADIA_HOSTING_WINDOW_CLASS");
        foreach (var console in consoles)
        {
            ShowWindow(console, SW_MINIMIZE);
        }

        return consoles.Count;
    }

    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();

    public static void KeyDown(ushort vk, bool extended = false) => SendKey(vk, extended ? KEYEVENTF_EXTENDEDKEY : 0);

    public static void KeyUp(ushort vk, bool extended = false) => SendKey(vk, KEYEVENTF_KEYUP | (extended ? KEYEVENTF_EXTENDEDKEY : 0));

    /// <summary>Presses and releases a key, like a user tapping it.</summary>
    public static void Press(ushort vk)
    {
        KeyDown(vk);
        Thread.Sleep(30);
        KeyUp(vk);
    }

    public static void Chord(ushort modifier, ushort vk)
    {
        KeyDown(modifier);
        Thread.Sleep(20);
        Press(vk);
        Thread.Sleep(20);
        KeyUp(modifier);
    }

    /// <summary>Left-clicks at a screen position (physical pixels).</summary>
    public static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(60);
        var inputs = stackalloc INPUT[2];
        inputs[0] = new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTDOWN } } };
        inputs[1] = new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTUP } } };
        SendInput(2, inputs, sizeof(INPUT));
        Thread.Sleep(120);
    }

    /// <summary>Clicks the center of a window after making sure no other window covers that point.</summary>
    public static void ClickCenter(nint hwnd, int offsetY = 0)
    {
        GetWindowRect(hwnd, out var rect);
        var x = rect.Left + (rect.Width / 2);
        var y = rect.Top + (rect.Height / 2) + offsetY;
        var root = GetAncestor(hwnd, 2 /* GA_ROOT */);
        if (TopLevelAt(x, y) != root)
        {
            MinimizeConsoleWindows();
            SetWindowPos(root, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            Thread.Sleep(200);
        }

        var covering = TopLevelAt(x, y);
        if (covering != root)
        {
            throw new InvalidOperationException($"({x},{y}) is covered by {Describe(covering)}; not clicking.");
        }

        Click(x, y);
    }

    public static string GetWindowText(nint hwnd)
    {
        var buffer = stackalloc char[1024];
        var length = GetWindowTextW(hwnd, buffer, 1024);
        return new string(buffer, 0, Math.Max(0, length));
    }

    public static string GetClassName(nint hwnd)
    {
        var buffer = stackalloc char[256];
        var length = GetClassNameW(hwnd, buffer, 256);
        return new string(buffer, 0, Math.Max(0, length));
    }

    /// <summary>Reads the full text of an edit control in another process (WM_GETTEXT).</summary>
    public static string GetControlText(nint hwnd)
    {
        SendMessageTimeoutW(hwnd, WM_GETTEXTLENGTH, 0, 0, 0, 5000, out var length);
        var size = (int)length + 1;
        var buffer = new char[size];
        fixed (char* pointer = buffer)
        {
            SendMessageTimeoutW(hwnd, WM_GETTEXT, size, pointer, 0, 10000, out var copied);
            return new string(pointer, 0, (int)copied);
        }
    }

    public static void SetControlText(nint hwnd, string text) => SendMessageTimeoutW(hwnd, WM_SETTEXT, 0, text, 0, 5000, out _);

    public static List<nint> TopLevelWindows(Func<nint, bool> predicate)
    {
        var result = new List<nint>();
        EnumWindows((hwnd, _) =>
        {
            if (predicate(hwnd))
            {
                result.Add(hwnd);
            }

            return true;
        }, 0);
        return result;
    }

    public static List<nint> ChildWindows(nint parent)
    {
        var result = new List<nint>();
        EnumChildWindows(parent, (hwnd, _) =>
        {
            result.Add(hwnd);
            return true;
        }, 0);
        return result;
    }

    public static int ProcessIdOf(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    public static string Describe(nint hwnd) => hwnd == 0 ? "(none)" : $"0x{hwnd:X} '{GetWindowText(hwnd)}' [{GetClassName(hwnd)}] pid {ProcessIdOf(hwnd)}";

    private static void SendKey(ushort vk, uint flags)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = (ushort)MapVirtualKeyW(vk, 0), dwFlags = flags } },
        };
        SendInput(1, &input, sizeof(INPUT));
    }
}
