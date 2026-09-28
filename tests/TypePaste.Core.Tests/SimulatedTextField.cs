using System.Text;
using TypePaste.Core.Input;

namespace TypePaste.Core.Tests;

/// <summary>
/// Minimal model of a text field receiving keyboard events: Unicode packets insert their code unit, Enter inserts a
/// line break, Tab inserts a tab, and layout keys are decoded through a reverse key map. Used to prove that the
/// planned keyboard events reproduce the original text exactly.
/// </summary>
internal sealed class SimulatedTextField
{
    private readonly StringBuilder _text = new();
    private readonly Dictionary<(ushort Vk, LayoutModifiers Modifiers), char> _layoutKeys = new();
    private bool _shift;
    private bool _control;
    private bool _alt;

    public string Text => _text.ToString();

    public List<string> Violations { get; } = [];

    public void AddLayoutKey(char c, LayoutKey key) => _layoutKeys[(key.VirtualKey, key.Modifiers)] = c;

    public void Apply(IEnumerable<KeyboardEvent> events)
    {
        foreach (var e in events)
        {
            Apply(e);
        }
    }

    public void Apply(KeyboardEvent e)
    {
        if (e.IsUnicode)
        {
            if (!e.IsKeyUp)
            {
                _text.Append((char)e.ScanCode);
            }

            return;
        }

        switch (e.VirtualKey)
        {
            case VirtualKeys.LeftShift:
                _shift = !e.IsKeyUp;
                return;
            case VirtualKeys.LeftControl:
                _control = !e.IsKeyUp;
                return;
            case VirtualKeys.LeftMenu:
                _alt = !e.IsKeyUp;
                return;
        }

        if (e.IsKeyUp)
        {
            return;
        }

        switch (e.VirtualKey)
        {
            case VirtualKeys.Return:
                _text.Append('\n');
                return;
            case VirtualKeys.Tab:
                _text.Append('\t');
                return;
        }

        var modifiers = (_shift ? LayoutModifiers.Shift : 0) | (_control ? LayoutModifiers.Control : 0) | (_alt ? LayoutModifiers.Alt : 0);
        if (_layoutKeys.TryGetValue((e.VirtualKey, modifiers), out var c))
        {
            _text.Append(c);
        }
        else
        {
            Violations.Add($"Unknown key {e}");
        }
    }

    /// <summary>What a text field is expected to contain after typing <paramref name="source"/>.</summary>
    public static string Expected(string source) =>
        new(source.Replace("\r\n", "\n").Replace('\r', '\n').Where(c => !KeystrokePlanner.IsUntypeableControl(c)).ToArray());
}

/// <summary>A tiny fake US-like layout: lowercase letters plain, uppercase letters and '!' with Shift, '@' with AltGr.</summary>
internal sealed class FakeLayout : IKeyboardLayoutMapper
{
    public bool TryMap(char character, out LayoutKey key)
    {
        switch (character)
        {
            case >= 'a' and <= 'z':
                key = new LayoutKey((ushort)char.ToUpperInvariant(character), 0x10, LayoutModifiers.None);
                return true;
            case >= 'A' and <= 'Z':
                key = new LayoutKey(character, 0x10, LayoutModifiers.Shift);
                return true;
            case ' ':
                key = new LayoutKey(VirtualKeys.Space, 0x39, LayoutModifiers.None);
                return true;
            case '!':
                key = new LayoutKey(0x31, 0x02, LayoutModifiers.Shift);
                return true;
            case '@':
                key = new LayoutKey(0x51, 0x10, LayoutModifiers.Control | LayoutModifiers.Alt);
                return true;
            default:
                key = default;
                return false;
        }
    }

    public void Register(SimulatedTextField field)
    {
        foreach (var c in "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ !@")
        {
            TryMap(c, out var key);
            field.AddLayoutKey(c, key);
        }
    }
}
