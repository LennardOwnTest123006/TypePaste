namespace TypePaste.E2E;

/// <summary>A text field TypePaste types into during a test.</summary>
internal interface ITypingTarget
{
    string Name { get; }

    void Prepare();

    int Length { get; }

    /// <summary>Waits for the field to contain <paramref name="source"/> (normalized) and fails with a description if it does not.</summary>
    void AssertContains(string source);

    /// <summary>Waits until the field stops changing and returns its normalized length.</summary>
    int SettledLength();
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

    public void AssertContains(string source)
    {
        // RichEdit reports "\r" line breaks through WM_GETTEXT and "\n" through .Text; compare normalized text.
        var expected = Samples.AsEditControl(source);
        var actual = Wait.Stable(() => Samples.AsEditControl(Text), TimeSpan.FromMilliseconds(700), TimeSpan.FromSeconds(30));
        Assert.TextEquals(expected, actual);
    }

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

    public void AssertContains(string source)
    {
        var expected = Samples.AsEditControl(source);
        var actual = Wait.Stable(() => Samples.AsEditControl(notepad.Text), TimeSpan.FromMilliseconds(700), TimeSpan.FromSeconds(30));
        Assert.TextEquals(expected, actual);
    }

    public int SettledLength() => Wait.Stable(() => Length, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
}

internal sealed class EdgeTypingTarget(EdgeTarget edge) : ITypingTarget
{
    public string Name => "Microsoft Edge <textarea>";

    public int Length => edge.State.Length;

    public void Prepare()
    {
        edge.Clear();
        edge.Focus();
    }

    public void AssertContains(string source)
    {
        var expected = Samples.AsTextArea(source);
        var want = (expected.Length, Samples.Fnv1a(expected));
        var got = Wait.Stable(() => edge.State, TimeSpan.FromSeconds(1.2), TimeSpan.FromSeconds(60));
        Assert.That(got == want, $"textarea has length {got.Length} hash {got.Hash}; expected length {want.Length} hash {want.Item2}");
    }

    public int SettledLength() => Wait.Stable(() => Length, TimeSpan.FromSeconds(1.2), TimeSpan.FromSeconds(30));
}
