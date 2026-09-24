using System.Diagnostics;

namespace TypePaste.E2E;

/// <summary>A text field TypePaste types into during a test.</summary>
internal interface ITypingTarget
{
    string Name { get; }

    void Prepare();

    int Length { get; }

    /// <summary>
    /// Waits until the field contains exactly <paramref name="source"/> (normalized the way the control stores text) and
    /// returns how long that took. Fails with a description of the first difference after <paramref name="timeout"/>.
    /// </summary>
    TimeSpan WaitForText(string source, TimeSpan timeout);

    /// <summary>Waits until the field stops changing and returns its normalized length.</summary>
    int SettledLength();
}

internal static class TargetWait
{
    /// <summary>Polls <paramref name="read"/> until it equals <paramref name="expected"/>.</summary>
    public static TimeSpan ForText(Func<string> read, string expected, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        string actual;
        while (true)
        {
            actual = read();
            if (string.Equals(actual, expected, StringComparison.Ordinal))
            {
                return watch.Elapsed;
            }

            if (watch.Elapsed > timeout)
            {
                break;
            }

            Thread.Sleep(actual.Length > expected.Length ? 0 : 100);
        }

        Assert.TextEquals(expected, actual);
        return watch.Elapsed;
    }
}

internal sealed class FormTarget(TargetHost host, TargetForm form, string name) : ITypingTarget
{
    public string Name => name;

    public TargetForm Form => form;

    public int Length => host.GetLength(form);

    public string Text => host.GetText(form);

    public void Prepare()
    {
        host.Clear(form);
        host.Focus(form);
    }

    // RichEdit stores "\r" line breaks and .Text reports "\n"; compare normalized text.
    public TimeSpan WaitForText(string source, TimeSpan timeout) =>
        TargetWait.ForText(() => Samples.AsEditControl(Text), Samples.AsEditControl(source), timeout);

    public int SettledLength() => Wait.Stable(() => Length, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
}

internal sealed class NotepadTypingTarget(NotepadTarget notepad) : ITypingTarget
{
    public string Name => $"Notepad ({notepad.EditClass})";

    public int Length => notepad.Text.Length;

    public void Prepare()
    {
        notepad.Clear();
        notepad.Focus();
    }

    public TimeSpan WaitForText(string source, TimeSpan timeout) =>
        TargetWait.ForText(() => Samples.AsEditControl(notepad.Text), Samples.AsEditControl(source), timeout);

    public int SettledLength() => Wait.Stable(() => Length, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
}

internal sealed class EdgeTypingTarget(EdgeTarget edge) : ITypingTarget
{
    public string Name => "Microsoft Edge <textarea>";

    public int Length => edge.State.Length;

    public (int Length, string Hash) State => edge.State;

    public void Prepare()
    {
        edge.Clear();
        edge.Focus();
    }

    public TimeSpan WaitForText(string source, TimeSpan timeout)
    {
        var expected = Samples.AsTextArea(source);
        var want = (expected.Length, Samples.Fnv1a(expected));
        var watch = Stopwatch.StartNew();
        var got = edge.State;
        while (got != want && watch.Elapsed < timeout)
        {
            Thread.Sleep(100);
            got = edge.State;
        }

        Assert.That(got == want, $"textarea has length {got.Length} hash {got.Hash}; expected length {want.Length} hash {want.Item2}");
        return watch.Elapsed;
    }

    public int SettledLength() => Wait.Stable(() => Length, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(60));
}
