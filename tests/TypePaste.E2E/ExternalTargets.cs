using System.Diagnostics;

namespace TypePaste.E2E;

/// <summary>Windows Notepad as a typing target.</summary>
internal sealed class NotepadTarget : IDisposable
{
    private readonly Process _process;

    public NotepadTarget()
    {
        var before = Process.GetProcessesByName("notepad").Select(p => p.Id).ToHashSet();
        _process = Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true })!;
        Window = Wait.Until(
            () => Win32.TopLevelWindows(h => Win32.IsWindowVisible(h) && Win32.GetClassName(h) == "Notepad" && !before.Contains(Win32.ProcessIdOf(h))).FirstOrDefault(),
            TimeSpan.FromSeconds(30), "Notepad window");
        Edit = Wait.Until(
            () => Win32.ChildWindows(Window).FirstOrDefault(h => Win32.GetClassName(h) is "Edit" or "RichEditD2DPT"),
            TimeSpan.FromSeconds(15), "Notepad edit control");
        EditClass = Win32.GetClassName(Edit);
    }

    public nint Window { get; }

    public nint Edit { get; }

    public string EditClass { get; }

    public string Text => Win32.GetControlText(Edit);

    public void Clear() => Win32.SetControlText(Edit, string.Empty);

    public void Focus()
    {
        Win32.ShowWindow(Window, Win32.SW_RESTORE);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            Win32.ClickCenter(Edit);
            Thread.Sleep(200);
            if (Win32.GetForegroundWindow() == Window)
            {
                return;
            }
        }

        throw new CheckFailedException($"Notepad did not become active (foreground: {Win32.Describe(Win32.GetForegroundWindow())})");
    }

    public void Dispose()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("notepad"))
            {
                if (Win32.ProcessIdOf(Window) == process.Id)
                {
                    process.Kill();
                }
            }

            if (!_process.HasExited)
            {
                _process.Kill();
            }
        }
        catch (Exception)
        {
            // Already closed.
        }
    }
}

/// <summary>A &lt;textarea&gt; in Microsoft Edge; the page publishes length and hash of its value in the window title.</summary>
internal sealed class EdgeTarget : IDisposable
{
    private const string Page = """
        <!doctype html>
        <html><head><meta charset="utf-8"><title>TPE2E|0|811c9dc5</title>
        <style>html,body{margin:0;height:100%;background:#fff}textarea{box-sizing:border-box;width:100%;height:100%;border:0;padding:16px;font:15px Consolas,monospace}</style>
        </head><body><textarea id="t" spellcheck="false" autofocus></textarea>
        <script>
        const t = document.getElementById('t');
        function hash(s){let h=0x811c9dc5;for(let i=0;i<s.length;i++){h^=s.charCodeAt(i);h=Math.imul(h,0x01000193)>>>0;}return h.toString(16).padStart(8,'0');}
        let timer; function publish(){document.title='TPE2E|'+t.value.length+'|'+hash(t.value);}
        t.addEventListener('input',()=>{clearTimeout(timer);timer=setTimeout(publish,80);});
        publish();
        </script></body></html>
        """;

    private readonly Process _process;
    private readonly string _profile;

    public EdgeTarget(string workDirectory)
    {
        var edge = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
        }.FirstOrDefault(File.Exists) ?? throw new CheckFailedException("Microsoft Edge is not installed");

        var page = Path.Combine(workDirectory, "edge-target.html");
        File.WriteAllText(page, Page);
        _profile = Path.Combine(workDirectory, "edge-profile");
        var start = new ProcessStartInfo(edge) { UseShellExecute = false };
        foreach (var argument in new[]
        {
            $"--app={new Uri(page).AbsoluteUri}", $"--user-data-dir={_profile}", "--no-first-run", "--no-default-browser-check",
            "--disable-sync", "--disable-features=msEdgeSidebarV2,EdgeCollections,msUndersideButton", "--window-position=40,80", "--window-size=900,560",
        })
        {
            start.ArgumentList.Add(argument);
        }

        _process = Process.Start(start)!;
        Window = Wait.Until(
            () => Win32.TopLevelWindows(h => Win32.IsWindowVisible(h) && Win32.GetWindowText(h).StartsWith("TPE2E|", StringComparison.Ordinal)).FirstOrDefault(),
            TimeSpan.FromSeconds(60), "Edge test page");
    }

    public nint Window { get; }

    /// <summary>(length, hash) of the textarea value.</summary>
    public (int Length, string Hash) State
    {
        get
        {
            var parts = Win32.GetWindowText(Window).Split('|');
            return parts.Length >= 3 && int.TryParse(parts[1], out var length) ? (length, parts[2].Trim()) : (-1, string.Empty);
        }
    }

    public void Focus()
    {
        Win32.ShowWindow(Window, Win32.SW_RESTORE);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            Win32.ClickCenter(Window, offsetY: 20);
            Thread.Sleep(300);
            if (Win32.GetForegroundWindow() == Window)
            {
                return;
            }
        }

        throw new CheckFailedException($"Edge did not become active (foreground: {Win32.Describe(Win32.GetForegroundWindow())})");
    }

    public void Clear()
    {
        Focus();
        Win32.Chord(Win32.VK_LCONTROL, Win32.VK_A);
        Thread.Sleep(100);
        Win32.Press(Win32.VK_DELETE);
        Wait.Until(() => State.Length == 0, TimeSpan.FromSeconds(10), "empty textarea");
    }

    public void Dispose()
    {
        try
        {
            Win32.PostMessageW(Window, Win32.WM_CLOSE, 0, 0);
            if (!_process.WaitForExit(5000))
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // Already closed.
        }
    }
}
