namespace TypePaste.Core.Input;

/// <summary>Modifier keys that must be held to produce a character on the active keyboard layout.</summary>
[Flags]
public enum LayoutModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
}

/// <summary>A physical key (plus modifiers) that produces a character on the active keyboard layout.</summary>
public readonly record struct LayoutKey(ushort VirtualKey, ushort ScanCode, LayoutModifiers Modifiers);

/// <summary>
/// Maps characters to physical keys of the target window's keyboard layout. Used by the
/// "keyboard layout" input method for applications that ignore Unicode keyboard packets.
/// </summary>
public interface IKeyboardLayoutMapper
{
    /// <summary>
    /// Returns the key that produces <paramref name="character"/> with a single key press.
    /// Returns false when the character is not on the layout, needs a dead key, or would be ambiguous.
    /// </summary>
    bool TryMap(char character, out LayoutKey key);
}
