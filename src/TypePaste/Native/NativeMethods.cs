using System.Runtime.InteropServices;

namespace TypePaste.Native;

/// <summary>Win32 declarations used by TypePaste.</summary>
internal static unsafe class NativeMethods
{
    // ---------------------------------------------------------------- Input

    public const uint INPUT_KEYBOARD = 1;
    public const uint INPUT_MOUSE = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;

        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint cInputs, INPUT* pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    public static extern nint GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern short VkKeyScanExW(char ch, nint dwhkl);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint MapVirtualKeyExW(uint uCode, uint uMapType, nint dwhkl);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte* lpKeyState, char* pwszBuff, int cchBuff, uint wFlags, nint dwhkl);

    public const uint MAPVK_VK_TO_VSC = 0;

    // ---------------------------------------------------------------- Hotkeys

    public const int WM_HOTKEY = 0x0312;
    public const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(nint hWnd, int id);

    // ---------------------------------------------------------------- Windows

    public const int WM_NULL = 0x0000;
    public const int WM_DESTROY = 0x0002;
    public const int WM_CLOSE = 0x0010;
    public const int WM_QUERYENDSESSION = 0x0011;
    public const int WM_ENDSESSION = 0x0016;
    public const int WM_SETTINGCHANGE = 0x001A;
    public const int WM_COPYDATA = 0x004A;
    public const int WM_CONTEXTMENU = 0x007B;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_LBUTTONDBLCLK = 0x0203;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_APP = 0x8000;

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const uint WS_POPUP = 0x80000000;

    public const uint GA_ROOT = 2;
    public const uint GA_ROOTOWNER = 3;

    public const int SW_RESTORE = 9;
    public const int SW_SHOWNOACTIVATE = 4;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;

    public const uint SMTO_ABORTIFHUNG = 0x0002;

    public delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct COPYDATASTRUCT
    {
        public nint dwData;
        public int cbData;
        public nint lpData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public nint hwndActive;
        public nint hwndFocus;
        public nint hwndCapture;
        public nint hwndMenuOwner;
        public nint hwndMoveSize;
        public nint hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [DllImport("user32.dll")]
    public static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint FindWindowW(string? lpClassName, string? lpWindowName);

    public const uint MSGFLT_ALLOW = 1;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ChangeWindowMessageFilterEx(nint hwnd, uint message, uint action, nint pChangeFilterStruct);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SendMessageTimeoutW(nint hWnd, uint msg, nint wParam, ref COPYDATASTRUCT lParam, uint fuFlags, uint uTimeout, out nint lpdwResult);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern nint GetAncestor(nint hwnd, uint gaFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out int lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO pgui);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowTextW(nint hWnd, char* lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClassNameW(nint hWnd, char* lpClassName, int nMaxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfoW(nint hMonitor, ref MONITORINFO lpmi);

    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint MONITOR_DEFAULTTOPRIMARY = 1;

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    public static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    public const int SM_CXSMICON = 49;
    public const int SM_CYSMICON = 50;

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandleW(string? lpModuleName);

    public static string GetWindowText(nint hwnd)
    {
        var buffer = stackalloc char[512];
        var length = GetWindowTextW(hwnd, buffer, 512);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    public static string GetClassName(nint hwnd)
    {
        var buffer = stackalloc char[256];
        var length = GetClassNameW(hwnd, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    // ---------------------------------------------------------------- Menus

    public const uint MF_STRING = 0x0000;
    public const uint MF_SEPARATOR = 0x0800;
    public const uint MF_CHECKED = 0x0008;
    public const uint MF_GRAYED = 0x0001;
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_RETURNCMD = 0x0100;
    public const uint TPM_NONOTIFY = 0x0080;
    public const uint TPM_BOTTOMALIGN = 0x0020;
    public const uint TPM_RIGHTALIGN = 0x0008;

    [DllImport("user32.dll")]
    public static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool AppendMenuW(nint hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    public static extern bool SetMenuDefaultItem(nint hMenu, uint uItem, uint fByPos);

    [DllImport("user32.dll")]
    public static extern int TrackPopupMenuEx(nint hMenu, uint uFlags, int x, int y, nint hwnd, nint lptpm);

    [DllImport("user32.dll")]
    public static extern bool DestroyMenu(nint hMenu);

    // ---------------------------------------------------------------- Icons / tray

    public const uint NIM_ADD = 0;
    public const uint NIM_MODIFY = 1;
    public const uint NIM_DELETE = 2;
    public const uint NIF_MESSAGE = 0x1;
    public const uint NIF_ICON = 0x2;
    public const uint NIF_TIP = 0x4;
    public const uint NIF_INFO = 0x10;
    public const uint NIIF_INFO = 0x1;
    public const uint NIIF_USER = 0x4;
    public const uint NIIF_LARGE_ICON = 0x20;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATAW
    {
        public int cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint CreateIconFromResourceEx(byte* presbits, int dwResSize, bool fIcon, uint dwVer, int cxDesired, int cyDesired, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(nint hIcon);

    // ---------------------------------------------------------------- DWM / theme

    public const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_BORDER_COLOR = 34;
    public const int DWMWA_CAPTION_COLOR = 35;

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
    public static extern int SetPreferredAppMode(int appMode);

    [DllImport("uxtheme.dll", EntryPoint = "#136", SetLastError = true)]
    public static extern void FlushMenuThemes();

    // ---------------------------------------------------------------- Timing

    public const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
    public const uint TIMER_ALL_ACCESS = 0x001F0003;
    public const uint INFINITE = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint CreateWaitableTimerExW(nint lpTimerAttributes, string? lpTimerName, uint dwFlags, uint dwDesiredAccess);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetWaitableTimer(nint hTimer, ref long lpDueTime, int lPeriod, nint pfnCompletionRoutine, nint lpArgToCompletionRoutine, bool fResume);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(nint hObject);

    [DllImport("winmm.dll")]
    public static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll")]
    public static extern uint timeEndPeriod(uint uPeriod);

    // ---------------------------------------------------------------- Processes / security

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint TOKEN_QUERY = 0x0008;
    public const int TokenIntegrityLevel = 25;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll")]
    public static extern nint GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool GetTokenInformation(nint tokenHandle, int tokenInformationClass, void* tokenInformation, int tokenInformationLength, out int returnLength);

    [DllImport("advapi32.dll")]
    public static extern byte* GetSidSubAuthorityCount(nint pSid);

    [DllImport("advapi32.dll")]
    public static extern uint* GetSidSubAuthority(nint pSid, uint nSubAuthority);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool QueryFullProcessImageNameW(nint hProcess, uint dwFlags, char* lpExeName, ref int lpdwSize);

    // ---------------------------------------------------------------- NT

    public const int SystemProcessInformation = 5;
    public const uint STATUS_INFO_LENGTH_MISMATCH = 0xC0000004;

    [DllImport("ntdll.dll")]
    public static extern uint NtQuerySystemInformation(int systemInformationClass, void* systemInformation, int systemInformationLength, out int returnLength);

    public static string? GetProcessImagePath(int processId)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == 0)
        {
            return null;
        }

        try
        {
            var buffer = stackalloc char[1024];
            var size = 1024;
            return QueryFullProcessImageNameW(process, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>Mandatory integrity level RID of a process (e.g. 0x2000 medium, 0x3000 high), or null if unknown.</summary>
    public static int? GetIntegrityLevel(nint processHandle)
    {
        if (!OpenProcessToken(processHandle, TOKEN_QUERY, out var token))
        {
            return null;
        }

        try
        {
            var buffer = stackalloc byte[256];
            if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, 256, out _))
            {
                return null;
            }

            // TOKEN_MANDATORY_LABEL { SID_AND_ATTRIBUTES Label { PSID Sid; DWORD Attributes; } }
            var sid = *(nint*)buffer;
            var count = *GetSidSubAuthorityCount(sid);
            return count == 0 ? null : (int)*GetSidSubAuthority(sid, (uint)(count - 1));
        }
        finally
        {
            CloseHandle(token);
        }
    }

    public static int? GetIntegrityLevel(int processId)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == 0)
        {
            return null;
        }

        try
        {
            return GetIntegrityLevel(process);
        }
        finally
        {
            CloseHandle(process);
        }
    }

    public static string DescribeLastError() => new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message;
}
