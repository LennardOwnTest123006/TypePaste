using System.Diagnostics;

namespace TypePaste.E2E;

/// <summary>The end-to-end scenarios: real hotkeys, real target apps, exact text comparison.</summary>
internal sealed class Scenarios(Report report, AppDriver app, TargetHost targets, string workDirectory)
{
    private FormTarget Plain => new(targets, targets.Plain, "WinForms TextBox");

    private FormTarget Rich => new(targets, targets.Rich, "WinForms RichTextBox");

    private static Dictionary<string, object> Settings(params (string Key, object Value)[] values)
    {
        var settings = new Dictionary<string, object>
        {
            ["Theme"] = "Light",
            ["ShowOverlay"] = true,
            ["CloseToTray"] = true,
        };
        foreach (var (key, value) in values)
        {
            settings[key] = value;
        }

        return settings;
    }

    private static TimeSpan TimeoutFor(int length, double minimumCharsPerSecond = 800) =>
        TimeSpan.FromSeconds(30 + (length / minimumCharsPerSecond));

    public void Run()
    {
        AppBasics();
        TypingIntoTextBox();
        TypingIntoRichEdit();
        TypingIntoNotepad();
        TypingIntoEdge();
        StoppingAndSafety();
        SpeedAndInputModes();
        UserInterface();
    }

    // ------------------------------------------------------------------ App

    private void AppBasics()
    {
        report.Area = "App";
        report.Check("starts, shows its window and registers F6", () =>
        {
            var watch = Stopwatch.StartNew();
            app.Start(Settings());
            var startup = watch.Elapsed;
            Thread.Sleep(800);
            report.Screenshot("main-window-empty");
            Assert.That(app.Status == "Ready", $"status is '{app.Status}': {app.StatusDetail}");
            Assert.That(app.StatusDetail.Contains("F6", StringComparison.Ordinal), $"detail: {app.StatusDetail}");
            return $"ready in {startup.TotalSeconds:0.0} s; status '{app.Status}' — {app.StatusDetail}";
        });

        report.Check("single instance: a second launch forwards the text and exits", () =>
        {
            var elapsed = app.LoadText(Samples.Multiline);
            Thread.Sleep(500);
            report.Screenshot("main-window-with-text");
            var instances = Process.GetProcessesByName("TypePaste").Length;
            Assert.That(instances == 1, $"{instances} TypePaste processes running");
            return $"text handed over in {elapsed.TotalMilliseconds:0} ms; statistics: {app.CharacterCount} characters";
        });
    }

    // ------------------------------------------------------------------ Typing into different apps

    /// <summary>
    /// Loads <paramref name="text"/>, clicks into the target, presses F6 and verifies the exact result. Also checks that
    /// the target has processed everything shortly after TypePaste reports completion (no hidden keystroke backlog).
    /// </summary>
    private string TypeWithHotkey(ITypingTarget target, string text, TimeSpan? timeout = null)
    {
        app.LoadText(text);
        app.Minimize();
        target.Prepare();
        var watch = Stopwatch.StartNew();
        Win32.Press(Win32.VK_F6);
        var status = app.WaitForResult(timeout ?? TimeoutFor(text.Length));
        var elapsed = watch.Elapsed;
        Assert.That(status == "Completed", $"status '{status}': {app.StatusDetail}");
        var lag = target.WaitForText(text, TimeSpan.FromSeconds(120));
        var total = watch.Elapsed;
        Assert.That(lag < TimeSpan.FromSeconds(3), $"exact text arrived, but {lag.TotalSeconds:0.0} s after TypePaste reported completion (backlog in the target)");
        return $"{text.Length:N0} chars exact in {total.TotalSeconds:0.00} s ≈ {text.Length / Math.Max(total.TotalSeconds, 0.001):N0} chars/s " +
               $"(TypePaste done after {elapsed.TotalSeconds:0.00} s)";
    }

    private void TypingIntoTextBox()
    {
        report.Area = "F6 → TextBox";
        targets.ShowOnly(targets.Plain, targets.Other);
        report.Check("multi-line text (CRLF, LF, blank lines, trailing spaces)", () => TypeWithHotkey(Plain, Samples.Multiline));
        report.Check("all printable ASCII + symbols", () => TypeWithHotkey(Plain, Samples.Symbols));
        report.Check("Unicode: accents, CJK, RTL, emoji, combining marks", () => TypeWithHotkey(Plain, Samples.Unicode));
        report.Check("tab characters", () => TypeWithHotkey(Plain, Samples.Tabs));
        report.Check("very long text (100,000 characters)", () => TypeWithHotkey(Plain, Samples.Long(100_000)));
    }

    private void TypingIntoRichEdit()
    {
        report.Area = "F6 → RichEdit";
        targets.ShowOnly(targets.Rich);
        report.Check("multi-line text", () => TypeWithHotkey(Rich, Samples.Multiline));
        report.Check("Unicode and emoji", () => TypeWithHotkey(Rich, Samples.Unicode));
        report.Check("long text (20,000 characters)", () => TypeWithHotkey(Rich, Samples.Long(20_000)));
    }

    private void TypingIntoNotepad()
    {
        report.Area = "F6 → Notepad";
        targets.ShowOnly();
        NotepadTarget? notepad = null;
        try
        {
            if (!report.Check("Notepad starts", () =>
            {
                notepad = new NotepadTarget();
                return $"edit control class {notepad.EditClass}";
            }))
            {
                return;
            }

            var target = new NotepadTypingTarget(notepad!);
            report.Check("multi-line text", () => TypeWithHotkey(target, Samples.Multiline));
            report.Check("symbols", () => TypeWithHotkey(target, Samples.Symbols));
            report.Check("Unicode and emoji", () => TypeWithHotkey(target, Samples.Unicode));
            report.Check("long text (30,000 characters)", () => TypeWithHotkey(target, Samples.Long(30_000)));
        }
        finally
        {
            notepad?.Dispose();
        }
    }

    private void TypingIntoEdge()
    {
        report.Area = "F6 → Edge";
        targets.ShowOnly();
        EdgeTarget? edge = null;
        try
        {
            if (!report.Check("Edge opens the test page", () =>
            {
                edge = new EdgeTarget(workDirectory);
                Thread.Sleep(1500);
                report.Screenshot("edge-target");
                return Win32.GetWindowText(edge.Window);
            }, informational: true))
            {
                return;
            }

            var target = new EdgeTypingTarget(edge!);
            report.Check("multi-line text", () => TypeWithHotkey(target, Samples.Multiline));
            report.Check("symbols", () => TypeWithHotkey(target, Samples.Symbols));
            report.Check("Unicode and emoji", () => TypeWithHotkey(target, Samples.Unicode));
            report.Check("long text (20,000 characters)", () => TypeWithHotkey(target, Samples.Long(20_000)));
            report.Check("tab characters stay in the textarea", () => TypeWithHotkey(target, Samples.Tabs), informational: true);
            report.Check("Esc stops typing in the browser without a backlog", () =>
            {
                var text = Samples.Long(50_000);
                app.LoadText(text);
                app.Minimize();
                target.Prepare();
                Win32.Press(Win32.VK_F6);
                Wait.Until(() => target.Length > 1500, TimeSpan.FromSeconds(60), "typing to start");
                var atEsc = target.Length;
                Win32.Press(Win32.VK_ESCAPE);
                var status = app.WaitForResult(TimeSpan.FromSeconds(10));
                var final = target.SettledLength();
                var expected = Samples.AsTextArea(text);
                var state = target.State;
                Assert.That(status == "Stopped", $"status '{status}': {app.StatusDetail}");
                Assert.That(final < expected.Length && state.Hash == Samples.Fnv1a(expected[..final]), "typed text is not an exact prefix");
                Assert.That(final - atEsc < 500, $"{final - atEsc} characters were still typed after Esc");
                return $"{atEsc:N0} chars when Esc was pressed, {final:N0} final (+{final - atEsc:N0}); prefix exact";
            });
        }
        finally
        {
            edge?.Dispose();
        }
    }

    // ------------------------------------------------------------------ Stopping and safety

    private void StoppingAndSafety()
    {
        report.Area = "Stop & safety";
        targets.ShowOnly(targets.Plain, targets.Other);

        report.Check("Esc stops typing immediately", () =>
        {
            var text = Samples.Long(400_000);
            app.LoadText(text);
            app.Minimize();
            Plain.Prepare();
            Win32.Press(Win32.VK_F6);
            Wait.Until(() => Plain.Length > 3000, TimeSpan.FromSeconds(30), "typing to start");
            report.Screenshot("overlay-typing-instant");
            var atEsc = Plain.Length;
            var watch = Stopwatch.StartNew();
            Win32.Press(Win32.VK_ESCAPE);
            var status = app.WaitForResult(TimeSpan.FromSeconds(10));
            var latency = watch.Elapsed;
            var final = Plain.SettledLength();
            var actual = Plain.Text;
            var expected = Samples.AsEditControl(text);
            Assert.That(status == "Stopped", $"status '{status}': {app.StatusDetail}");
            Assert.That(actual.Length < expected.Length, "everything was typed despite Esc");
            Assert.That(expected.StartsWith(actual, StringComparison.Ordinal), $"typed text is not an exact prefix (at {Assert.Snippet(actual, actual.Length - 1)})");
            Assert.That(latency < TimeSpan.FromSeconds(2), $"stopping took {latency.TotalMilliseconds:0} ms");
            return $"stopped within {latency.TotalMilliseconds:0} ms; {atEsc:N0} chars when Esc was pressed, {final:N0} final (+{final - atEsc:N0} already queued); prefix exact";
        });

        report.Check("switching to another window stops typing (no text lands in the wrong app)", () =>
        {
            app.Start(Settings(("Speed", "Fast")));
            var text = Samples.Long(20_000);
            app.LoadText(text);
            app.Minimize();
            targets.Clear(targets.Other);
            Plain.Prepare();
            Win32.Press(Win32.VK_F6);
            Wait.Until(() => Plain.Length > 200, TimeSpan.FromSeconds(30), "typing to start");
            targets.Focus(targets.Other);
            var status = app.WaitForResult(TimeSpan.FromSeconds(10));
            var stray = Wait.Stable(() => targets.GetLength(targets.Other), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
            var actual = Plain.Text;
            Assert.That(status == "Stopped", $"status '{status}': {app.StatusDetail}");
            Assert.That(app.StatusDetail.Contains("Another window", StringComparison.Ordinal), $"detail: {app.StatusDetail}");
            Assert.That(Samples.AsEditControl(text).StartsWith(actual, StringComparison.Ordinal), "typed text is not an exact prefix");
            Assert.That(stray <= 2, $"{stray} characters landed in the other window");
            return $"stopped after {actual.Length:N0} chars; {stray} stray characters in the other window — {app.StatusDetail}";
        });

        report.Check("closing the target window stops typing", () =>
        {
            var closable = targets.CreateForm("E2E closable target", new System.Drawing.Rectangle(40, 140, 420, 300));
            var target = new FormTarget(targets, closable, "closable");
            app.LoadText(Samples.Long(20_000));
            app.Minimize();
            target.Prepare();
            Win32.Press(Win32.VK_F6);
            Wait.Until(() => target.Length > 100, TimeSpan.FromSeconds(30), "typing to start");
            targets.Invoke(closable.Close);
            var status = app.WaitForResult(TimeSpan.FromSeconds(10));
            Assert.That(status == "Stopped", $"status '{status}': {app.StatusDetail}");
            Assert.That(app.StatusDetail.Contains("closed", StringComparison.Ordinal), $"detail: {app.StatusDetail}");
            return app.StatusDetail;
        });

        report.Check("pressing F6 again while typing never types twice", () =>
        {
            app.Start(Settings());
            var text = Samples.Long(60_000);
            app.LoadText(text);
            app.Minimize();
            Plain.Prepare();
            Win32.Press(Win32.VK_F6);
            Thread.Sleep(150);
            Win32.Press(Win32.VK_F6);
            Thread.Sleep(300);
            Win32.Press(Win32.VK_F6);
            var status = app.WaitForResult(TimeoutFor(text.Length));
            Assert.That(status == "Completed", $"status '{status}': {app.StatusDetail}");
            Plain.WaitForText(text, TimeSpan.FromSeconds(30));
            return $"{text.Length:N0} chars typed exactly once";
        });

        report.Check("F6 while TypePaste itself is active does not type into its own editor", () =>
        {
            app.LoadText(Samples.Multiline);
            app.Restore();
            Win32.ClickCenter(app.MainWindow);
            var before = app.EditorText;
            Win32.Press(Win32.VK_F6);
            Thread.Sleep(1200);
            report.Screenshot("error-own-window");
            var after = app.EditorText;
            Assert.That(before == after, "the editor text changed");
            Assert.That(app.Status == "Choose where to type", $"status '{app.Status}'");
            return $"'{app.Status}': {app.StatusDetail}";
        });

        report.Check("F6 with an empty editor explains what to do", () =>
        {
            app.LoadText(string.Empty);
            app.Minimize();
            Plain.Prepare();
            Win32.Press(Win32.VK_F6);
            Thread.Sleep(1000);
            Assert.That(app.Status == "Nothing to type", $"status '{app.Status}'");
            Assert.That(Plain.Length == 0, "text was typed");
            return $"'{app.Status}': {app.StatusDetail}";
        });
    }

    // ------------------------------------------------------------------ Modes

    private void SpeedAndInputModes()
    {
        report.Area = "Modes";
        targets.ShowOnly(targets.Plain);

        report.Check("Fast speed (~500 chars/s)", () =>
        {
            app.Start(Settings(("Speed", "Fast")));
            return TypeWithHotkey(Plain, Samples.Long(3_000), TimeoutFor(3_000, 100));
        });

        report.Check("Steady speed (~65 chars/s)", () =>
        {
            app.Start(Settings(("Speed", "Steady")));
            return TypeWithHotkey(Plain, Samples.Unicode, TimeoutFor(Samples.Unicode.Length, 20));
        });

        report.Check("Custom delay 0.5 ms between characters", () =>
        {
            app.Start(Settings(("Speed", "Custom"), ("CustomDelayMs", 0.5)));
            return TypeWithHotkey(Plain, Samples.Long(4_000), TimeoutFor(4_000, 200));
        });

        report.Check("pause after line breaks", () =>
        {
            app.Start(Settings(("LineBreakDelayMs", 150)));
            var watch = Stopwatch.StartNew();
            var details = TypeWithHotkey(Plain, "one\ntwo\nthree\nfour\nfive");
            Assert.That(watch.Elapsed >= TimeSpan.FromMilliseconds(600), $"took only {watch.Elapsed.TotalMilliseconds:0} ms");
            return details;
        });

        report.Check("keyboard-layout input method (compatibility mode)", () =>
        {
            app.Start(Settings(("InputMethod", "KeyboardLayout")));
            return TypeWithHotkey(Plain, Samples.Symbols + "\n" + Samples.Unicode);
        });

        report.Check("Shift+Enter line breaks", () =>
        {
            app.Start(Settings(("LineBreak", "ShiftEnter")));
            return TypeWithHotkey(Plain, Samples.Multiline);
        });

        report.Check("custom hotkey Ctrl+Shift+F9 waits until the keys are released", () =>
        {
            app.Start(Settings(("StartHotkey", "Ctrl+Shift+F9")));
            Assert.That(app.Status == "Ready", $"status '{app.Status}': {app.StatusDetail}");
            var text = Samples.Multiline;
            app.LoadText(text);
            app.Minimize();
            Plain.Prepare();
            Win32.KeyDown(Win32.VK_LCONTROL);
            Win32.KeyDown(Win32.VK_LSHIFT);
            Win32.Press(Win32.VK_F9);
            Thread.Sleep(500);
            var whileHeld = Plain.Length;
            Win32.KeyUp(Win32.VK_LSHIFT);
            Win32.KeyUp(Win32.VK_LCONTROL);
            var status = app.WaitForResult(TimeoutFor(text.Length));
            Assert.That(whileHeld == 0, $"{whileHeld} characters were typed while modifiers were held");
            Assert.That(status == "Completed", $"status '{status}': {app.StatusDetail}");
            Plain.WaitForText(text, TimeSpan.FromSeconds(30));
            return "nothing typed while Ctrl+Shift were held; exact text after release";
        });
    }

    // ------------------------------------------------------------------ UI

    private void UserInterface()
    {
        report.Area = "UI";
        targets.ShowOnly(targets.Plain);

        report.Check("Test typing button: built-in test pad verifies exact text", () =>
        {
            app.Start(Settings());
            app.LoadText(Samples.Multiline + "\n" + Samples.Unicode + "\n" + Samples.Symbols);
            app.Restore();
            Win32.ClickCenter(app.MainWindow);
            app.Invoke("TestButton");
            string title = string.Empty;
            Wait.Until(() => (title = app.FindAnywhere("TestPadResultTitle")?.Current.Name ?? string.Empty).Length > 0, TimeSpan.FromSeconds(60), "test pad result");
            Thread.Sleep(400);
            report.Screenshot("test-pad-result");
            var detail = app.FindAnywhere("TestPadResultDetail")?.Current.Name;
            if (app.FindAnywhere("TestPadClose") is { } close)
            {
                ((System.Windows.Automation.InvokePattern)close.GetCurrentPattern(System.Windows.Automation.InvokePattern.Pattern)).Invoke();
            }

            Assert.That(title == "Perfect match", $"test pad says '{title}': {detail}");
            return $"{title}: {detail}";
        });

        report.Check("settings panel opens and closes", () =>
        {
            app.Restore();
            app.Invoke("SettingsButton");
            Thread.Sleep(800);
            report.Screenshot("settings-light");
            app.Invoke("SettingsCloseButton");
            Thread.Sleep(500);
            return "opened and closed";
        });

        report.Check("Start button counts down, then types into the clicked field", () =>
        {
            app.Start(Settings(("StartCountdownSeconds", 3)));
            var text = Samples.Multiline;
            app.LoadText(text);
            app.Restore();
            Win32.ClickCenter(app.MainWindow);
            targets.Clear(targets.Plain);
            app.Invoke("StartButton");
            Thread.Sleep(700);
            report.Screenshot("start-countdown");
            targets.Focus(targets.Plain);
            var status = app.WaitForResult(TimeSpan.FromSeconds(30));
            Assert.That(status == "Completed", $"status '{status}': {app.StatusDetail}");
            Plain.WaitForText(text, TimeSpan.FromSeconds(30));
            Thread.Sleep(300);
            report.Screenshot("overlay-completed");
            return "countdown → typed exactly";
        });

        report.Check("progress overlay while typing (Steady)", () =>
        {
            app.Start(Settings(("Speed", "Steady")));
            app.LoadText(Samples.Long(2_000));
            app.Minimize();
            Plain.Prepare();
            Win32.Press(Win32.VK_F6);
            Thread.Sleep(2500);
            report.Screenshot("overlay-typing");
            Win32.Press(Win32.VK_ESCAPE);
            var status = app.WaitForResult(TimeSpan.FromSeconds(10));
            Thread.Sleep(300);
            report.Screenshot("overlay-stopped");
            Assert.That(status == "Stopped", $"status '{status}'");
            return app.StatusDetail;
        });

        report.Check("dark theme", () =>
        {
            app.Start(Settings(("Theme", "Dark"), ("EditorFont", "Monospace")));
            app.LoadText(Samples.Multiline + "\n" + Samples.Unicode);
            app.Restore();
            Thread.Sleep(600);
            report.Screenshot("main-window-dark");
            app.Invoke("SettingsButton");
            Thread.Sleep(800);
            report.Screenshot("settings-dark");
            app.Invoke("SettingsCloseButton");
            return "rendered";
        });

        report.Check("launch minimized to the tray: F6 works without the window", () =>
        {
            var text = Samples.Unicode;
            File.WriteAllText(Path.Combine(app.DataDirectory, "text.txt"), text, new System.Text.UTF8Encoding(false));
            app.Start(Settings(("RememberText", true), ("StartMinimizedToTray", true)), tray: true);
            Assert.That(!Win32.IsWindowVisible(app.MainWindow), "main window is visible");
            Plain.Prepare();
            Win32.Press(Win32.VK_F6);
            var expectedLength = Samples.AsEditControl(text).Length;
            Wait.Until(() => Plain.Length >= expectedLength, TimeoutFor(text.Length), "remembered text to be typed");
            Plain.WaitForText(text, TimeSpan.FromSeconds(30));
            return "hidden window, remembered text typed exactly";
        });

        // Leave TypePaste running so the uninstaller has to close it.
        app.Start(Settings());
    }
}
