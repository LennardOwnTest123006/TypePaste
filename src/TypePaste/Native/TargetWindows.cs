using System.Diagnostics;
using TypePaste.Core.Typing;
using static TypePaste.Native.NativeMethods;

namespace TypePaste.Native;

/// <summary>Why the active window cannot be used as a typing target.</summary>
internal enum TargetProblem
{
    None,
    NoActiveWindow,
    DesktopOrTaskbar,
    OwnWindow,
    Elevated,
}

internal sealed record TargetCapture(TypingTarget? Target, TargetProblem Problem, string? ProcessName = null)
{
    public bool IsValid => Target is not null && Problem == TargetProblem.None;
}

/// <summary>Finds and inspects the window that should receive the typed text.</summary>
internal static class TargetWindows
{
    private const int MediumIntegrity = 0x2000;
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
    };

    private static readonly Lazy<int?> OwnIntegrity = new(() => GetIntegrityLevel(GetCurrentProcess()));

    /// <summary>Captures the current foreground window as the typing target.</summary>
    public static TargetCapture CaptureForeground() => Capture(GetForegroundWindow());

    public static TargetCapture Capture(nint window)
    {
        if (window == 0 || !IsWindow(window))
        {
            return new TargetCapture(null, TargetProblem.NoActiveWindow);
        }

        var root = GetAncestor(window, GA_ROOT);
        if (root != 0)
        {
            window = root;
        }

        var windowThread = GetWindowThreadProcessId(window, out var processId);
        if (windowThread == 0)
        {
            return new TargetCapture(null, TargetProblem.NoActiveWindow);
        }

        // Keyboard input goes to the thread that owns the focused control (it can differ from the window's thread,
        // e.g. for UWP apps hosted in ApplicationFrameHost).
        var info = new GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };
        var inputThread = windowThread;
        var inputProcess = processId;
        if (GetGUIThreadInfo(windowThread, ref info) && info.hwndFocus != 0)
        {
            var focusThread = GetWindowThreadProcessId(info.hwndFocus, out var focusProcess);
            if (focusThread != 0)
            {
                inputThread = focusThread;
                inputProcess = focusProcess;
            }
        }

        var target = new TypingTarget(window, inputProcess, (int)inputThread, GetWindowText(window), GetProcessName(processId));

        if (processId == Environment.ProcessId)
        {
            return new TargetCapture(target, TargetProblem.OwnWindow, target.ProcessName);
        }

        if (ShellClasses.Contains(GetClassName(window)))
        {
            return new TargetCapture(target, TargetProblem.DesktopOrTaskbar, target.ProcessName);
        }

        // Windows (UIPI) silently drops simulated input sent to apps with a higher integrity level.
        var targetIntegrity = GetIntegrityLevel(processId);
        var ownIntegrity = OwnIntegrity.Value ?? MediumIntegrity;
        if (targetIntegrity is { } level && level > ownIntegrity)
        {
            return new TargetCapture(target, TargetProblem.Elevated, target.ProcessName);
        }

        return new TargetCapture(target, TargetProblem.None, target.ProcessName);
    }

    /// <summary>Whether TypePaste itself runs as administrator.</summary>
    public static bool IsRunningElevated => (OwnIntegrity.Value ?? MediumIntegrity) > MediumIntegrity;

    private static string? GetProcessName(int processId)
    {
        var path = GetProcessImagePath(processId);
        if (path is not null)
        {
            return Path.GetFileNameWithoutExtension(path);
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
