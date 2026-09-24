using TypePaste.Core.Typing;

namespace TypePaste.Core.Input;

/// <summary>What a planned stroke represents.</summary>
public enum StrokeKind
{
    Character,
    LineBreak,
    Tab,

    /// <summary>An invisible control character that cannot be typed (for example NUL or BEL); nothing is sent.</summary>
    Skipped,
}

/// <summary>Result of planning one logical character.</summary>
/// <param name="Length">Number of UTF-16 code units consumed from the source text.</param>
/// <param name="Kind">What was planned.</param>
public readonly record struct Stroke(int Length, StrokeKind Kind);

/// <summary>
/// Converts text into keyboard events, one logical character at a time. Line breaks (CRLF, CR or LF) become a
/// single Enter (or Shift+Enter) key press; surrogate pairs are always planned together so they are never split
/// across batches; everything else is typed exactly as it appears in the text.
/// </summary>
public sealed class KeystrokePlanner
{
    private readonly TypingOptions _options;
    private readonly IKeyboardLayoutMapper? _layout;
    private readonly bool _capsLockOn;

    /// <param name="options">Typing options (line break, tab and input method).</param>
    /// <param name="layout">Keyboard layout mapper, used only for <see cref="InputMethod.KeyboardLayout"/>.</param>
    /// <param name="capsLockOn">Whether Caps Lock is on (letters are then typed as Unicode in layout mode).</param>
    public KeystrokePlanner(TypingOptions options, IKeyboardLayoutMapper? layout = null, bool capsLockOn = false)
    {
        _options = options;
        _layout = options.Method == InputMethod.KeyboardLayout ? layout : null;
        _capsLockOn = capsLockOn;
    }

    /// <summary>True for characters TypePaste cannot type (C0 controls other than tab/CR/LF, and DEL).</summary>
    public static bool IsUntypeableControl(char c) => c is < ' ' and not '\t' and not '\r' and not '\n' or '\u007F';

    /// <summary>Counts characters that will be skipped because they cannot be typed.</summary>
    public static int CountUntypeableControls(ReadOnlySpan<char> text)
    {
        var count = 0;
        foreach (var c in text)
        {
            if (IsUntypeableControl(c))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Appends the keyboard events for the logical character starting at <paramref name="index"/>.
    /// </summary>
    public Stroke Plan(string text, int index, List<KeyboardEvent> output)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, text.Length);

        var c = text[index];

        switch (c)
        {
            case '\r':
            {
                AppendLineBreak(output);
                var length = index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
                return new Stroke(length, StrokeKind.LineBreak);
            }

            case '\n':
                AppendLineBreak(output);
                return new Stroke(1, StrokeKind.LineBreak);

            case '\t':
                if (_options.Tab == TabStyle.TabKey)
                {
                    AppendKeyPress(output, VirtualKeys.Tab, VirtualKeys.ScanTab);
                }
                else
                {
                    AppendUnicode(output, c);
                }

                return new Stroke(1, StrokeKind.Tab);
        }

        if (IsUntypeableControl(c))
        {
            return new Stroke(1, StrokeKind.Skipped);
        }

        if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            AppendUnicode(output, c);
            AppendUnicode(output, text[index + 1]);
            return new Stroke(2, StrokeKind.Character);
        }

        if (_layout is not null && !char.IsSurrogate(c) && !(_capsLockOn && char.IsLetter(c)) &&
            _layout.TryMap(c, out var key))
        {
            AppendLayoutKey(output, key);
        }
        else
        {
            AppendUnicode(output, c);
        }

        return new Stroke(1, StrokeKind.Character);
    }

    private void AppendLineBreak(List<KeyboardEvent> output)
    {
        if (_options.LineBreak == LineBreakStyle.ShiftEnter)
        {
            output.Add(KeyboardEvent.KeyDown(VirtualKeys.LeftShift, VirtualKeys.ScanLeftShift));
            AppendKeyPress(output, VirtualKeys.Return, VirtualKeys.ScanReturn);
            output.Add(KeyboardEvent.KeyUp(VirtualKeys.LeftShift, VirtualKeys.ScanLeftShift));
        }
        else
        {
            AppendKeyPress(output, VirtualKeys.Return, VirtualKeys.ScanReturn);
        }
    }

    private static void AppendLayoutKey(List<KeyboardEvent> output, LayoutKey key)
    {
        var shift = (key.Modifiers & LayoutModifiers.Shift) != 0;
        var control = (key.Modifiers & LayoutModifiers.Control) != 0;
        var alt = (key.Modifiers & LayoutModifiers.Alt) != 0;

        if (control)
        {
            output.Add(KeyboardEvent.KeyDown(VirtualKeys.LeftControl, VirtualKeys.ScanLeftControl));
        }

        if (alt)
        {
            output.Add(KeyboardEvent.KeyDown(VirtualKeys.LeftMenu, VirtualKeys.ScanLeftAlt));
        }

        if (shift)
        {
            output.Add(KeyboardEvent.KeyDown(VirtualKeys.LeftShift, VirtualKeys.ScanLeftShift));
        }

        AppendKeyPress(output, key.VirtualKey, key.ScanCode);

        if (shift)
        {
            output.Add(KeyboardEvent.KeyUp(VirtualKeys.LeftShift, VirtualKeys.ScanLeftShift));
        }

        if (alt)
        {
            output.Add(KeyboardEvent.KeyUp(VirtualKeys.LeftMenu, VirtualKeys.ScanLeftAlt));
        }

        if (control)
        {
            output.Add(KeyboardEvent.KeyUp(VirtualKeys.LeftControl, VirtualKeys.ScanLeftControl));
        }
    }

    private static void AppendKeyPress(List<KeyboardEvent> output, ushort vk, ushort scan)
    {
        output.Add(KeyboardEvent.KeyDown(vk, scan));
        output.Add(KeyboardEvent.KeyUp(vk, scan));
    }

    private static void AppendUnicode(List<KeyboardEvent> output, char c)
    {
        output.Add(KeyboardEvent.UnicodeDown(c));
        output.Add(KeyboardEvent.UnicodeUp(c));
    }
}
