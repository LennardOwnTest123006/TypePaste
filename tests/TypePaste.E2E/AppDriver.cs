using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;

namespace TypePaste.E2E;

/// <summary>Starts, controls and inspects the installed TypePaste app (through its UI, like a user).</summary>
internal sealed class AppDriver : IDisposable
{
    public const string MessageWindowClass = "TypePaste.MessageWindow";

    private readonly string _exePath;
    private readonly string _dataDirectory;
    private readonly string _workDirectory;
    private AutomationElement? _mainElement;
    private int _loadCounter;

    public AppDriver(string exePath, string workDirectory)
    {
        _exePath = exePath;
        _workDirectory = workDirectory;
        _dataDirectory = Path.Combine(workDirectory, "profile");
        Directory.CreateDirectory(_dataDirectory);
    }

    public Process? Process { get; private set; }

    public nint MainWindow { get; private set; }

    public string DataDirectory => _dataDirectory;

    /// <summary>Starts TypePaste with the given settings (JSON object properties) and waits until it is ready.</summary>
    public void Start(Dictionary<string, object>? settings = null, bool tray = false)
    {
        Stop();
        var json = JsonSerializer.Serialize(settings ?? new Dictionary<string, object>(), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(_dataDirectory, "settings.json"), json, new UTF8Encoding(false));

        var start = new ProcessStartInfo(_exePath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(_exePath)! };
        start.Environment["TYPEPASTE_DATA_DIR"] = _dataDirectory;
        if (tray)
        {
            start.ArgumentList.Add("--tray");
        }

        Process = Process.Start(start) ?? throw new InvalidOperationException("TypePaste did not start.");
        Wait.Until(() => FindMessageWindow() != 0, TimeSpan.FromSeconds(40), "TypePaste message window");
        MainWindow = Wait.Until(FindMainWindow, TimeSpan.FromSeconds(20), "TypePaste main window");
        _mainElement = null;
        if (!tray)
        {
            Wait.Until(() => Win32.IsWindowVisible(MainWindow), TimeSpan.FromSeconds(20), "visible main window");
            Wait.Until(() => Status is { Length: > 0 }, TimeSpan.FromSeconds(20), "status text");
        }
        else
        {
            Thread.Sleep(1500);
        }
    }

    /// <summary>Asks TypePaste to exit through its message window (the same path the installer uses).</summary>
    public void Stop()
    {
        if (Process is null)
        {
            return;
        }

        try
        {
            if (!Process.HasExited)
            {
                var window = FindMessageWindow();
                if (window != 0)
                {
                    Win32.PostMessageW(window, Win32.WM_CLOSE, 0, 0);
                }

                if (!Process.WaitForExit(10_000))
                {
                    Log.Line("  TypePaste did not exit after WM_CLOSE; killing it.");
                    Process.Kill();
                    Process.WaitForExit(5000);
                }
            }
        }
        finally
        {
            Process.Dispose();
            Process = null;
            MainWindow = 0;
            _mainElement = null;
        }
    }

    public bool HasExited => Process is null || Process.HasExited;

    public nint FindMessageWindow()
    {
        if (Process is null)
        {
            return 0;
        }

        var pid = Process.Id;
        return Win32.TopLevelWindows(h => Win32.GetClassName(h) == MessageWindowClass && Win32.ProcessIdOf(h) == pid).FirstOrDefault();
    }

    /// <summary>Loads text into the editor the way a user would double-click a file: a second instance forwards it.</summary>
    public TimeSpan LoadText(string text)
    {
        var file = Path.Combine(_workDirectory, $"load-{++_loadCounter}.txt");
        File.WriteAllText(file, text, new UTF8Encoding(false));
        var watch = Stopwatch.StartNew();
        var start = new ProcessStartInfo(_exePath) { UseShellExecute = false };
        start.Environment["TYPEPASTE_DATA_DIR"] = _dataDirectory;
        start.ArgumentList.Add("--load");
        start.ArgumentList.Add(file);
        using var second = Process.Start(start)!;
        if (!second.WaitForExit(20_000))
        {
            second.Kill();
            throw new InvalidOperationException("The second TypePaste instance did not exit (single-instance forwarding failed).");
        }

        if (second.ExitCode != 0)
        {
            throw new InvalidOperationException($"The second TypePaste instance exited with code {second.ExitCode}.");
        }

        // Wait for the editor statistics to reflect the new text.
        var expected = System.Globalization.StringInfo.ParseCombiningCharacters(text).Length;
        Wait.Until(() => CharacterCount == expected, TimeSpan.FromSeconds(20), $"editor to show {expected} characters (shows '{Element("CharacterCount")?.Current.Name}')");
        return watch.Elapsed;
    }

    public void Minimize()
    {
        Win32.ShowWindow(MainWindow, Win32.SW_MINIMIZE);
        Thread.Sleep(300);
    }

    public void Restore()
    {
        Win32.ShowWindow(MainWindow, Win32.SW_RESTORE);
        Thread.Sleep(300);
        Win32.SetForegroundWindow(MainWindow);
        Thread.Sleep(300);
    }

    public string Status => Element("StatusText")?.Current.Name ?? string.Empty;

    public string StatusDetail => Element("StatusDetail")?.Current.Name ?? string.Empty;

    public int CharacterCount
    {
        get
        {
            var text = Element("CharacterCount")?.Current.Name ?? string.Empty;
            var digits = new string(text.TakeWhile(c => !char.IsWhiteSpace(c)).Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var count) ? count : -1;
        }
    }

    public string EditorText => Element("EditorTextBox") is { } editor && editor.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern)
        ? ((ValuePattern)pattern).Current.Value
        : string.Empty;

    public void Invoke(string automationId)
    {
        var element = Element(automationId) ?? throw new InvalidOperationException($"Element '{automationId}' not found.");
        ((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
    }

    public void Select(string automationId)
    {
        var element = Element(automationId) ?? throw new InvalidOperationException($"Element '{automationId}' not found.");
        ((SelectionItemPattern)element.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
    }

    /// <summary>Finds an element in any TypePaste window (main window, test pad, overlay).</summary>
    public AutomationElement? FindAnywhere(string automationId)
    {
        if (Process is null)
        {
            return null;
        }

        var condition = new AndCondition(
            new PropertyCondition(AutomationElement.ProcessIdProperty, Process.Id),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
        foreach (AutomationElement window in AutomationElement.RootElement.FindAll(TreeScope.Children, condition))
        {
            var found = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Waits for a typing run to finish: the status leaves "Ready"/"Typing…" and settles.</summary>
    public string WaitForResult(TimeSpan timeout)
    {
        var final = string.Empty;
        Wait.Until(() =>
        {
            final = Status;
            return final.Length > 0 && final != "Ready" && !final.StartsWith("Typing", StringComparison.Ordinal)
                && !final.StartsWith("Starting", StringComparison.Ordinal) && !final.StartsWith("Preparing", StringComparison.Ordinal);
        }, timeout, "typing result");
        return final;
    }

    public void Dispose() => Stop();

    private nint FindMainWindow()
    {
        if (Process is null)
        {
            return 0;
        }

        var pid = Process.Id;
        return Win32.TopLevelWindows(h => Win32.ProcessIdOf(h) == pid && Win32.GetWindowText(h) == "TypePaste" && Win32.GetClassName(h).StartsWith("HwndWrapper", StringComparison.Ordinal))
            .FirstOrDefault();
    }

    private AutomationElement? Element(string automationId)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                _mainElement ??= AutomationElement.FromHandle(MainWindow);
                return _mainElement.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
            }
            catch (ElementNotAvailableException)
            {
                _mainElement = null;
            }
        }

        return null;
    }
}

/// <summary>Polling helper.</summary>
internal static class Wait
{
    public static void Until(Func<bool> condition, TimeSpan timeout, string what)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                if (condition())
                {
                    return;
                }
            }
            catch (Exception) when (watch.Elapsed < timeout)
            {
                // Transient (e.g. UI Automation while windows change); retry.
            }

            if (watch.Elapsed > timeout)
            {
                throw new TimeoutException($"Timed out after {timeout.TotalSeconds:0} s waiting for {what}.");
            }

            Thread.Sleep(100);
        }
    }

    public static T Until<T>(Func<T> probe, TimeSpan timeout, string what)
        where T : struct, IEquatable<T>
    {
        T value = default;
        Until(() => !(value = probe()).Equals(default), timeout, what);
        return value;
    }

    /// <summary>Waits until a value stops changing for <paramref name="quiet"/>.</summary>
    public static T Stable<T>(Func<T> probe, TimeSpan quiet, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        var last = probe();
        var lastChange = watch.Elapsed;
        while (watch.Elapsed < timeout)
        {
            Thread.Sleep(100);
            var current = probe();
            if (!EqualityComparer<T>.Default.Equals(current, last))
            {
                last = current;
                lastChange = watch.Elapsed;
            }
            else if (watch.Elapsed - lastChange >= quiet)
            {
                break;
            }
        }

        return last;
    }
}
