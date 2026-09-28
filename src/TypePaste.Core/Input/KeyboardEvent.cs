namespace TypePaste.Core.Input;

/// <summary>
/// Flags of a simulated keyboard event. The values intentionally match the Win32
/// <c>KEYEVENTF_*</c> constants so the Windows layer can pass them through unchanged.
/// </summary>
[Flags]
public enum KeyEventFlags : uint
{
    None = 0,
    ExtendedKey = 0x0001,
    KeyUp = 0x0002,
    Unicode = 0x0004,
    ScanCode = 0x0008,
}

/// <summary>
/// A single platform-neutral keyboard event (one key going down or up).
/// </summary>
/// <param name="VirtualKey">Virtual-key code, or 0 for Unicode packets.</param>
/// <param name="ScanCode">Hardware scan code, or the UTF-16 code unit for Unicode packets.</param>
/// <param name="Flags">Event flags.</param>
public readonly record struct KeyboardEvent(ushort VirtualKey, ushort ScanCode, KeyEventFlags Flags)
{
    public bool IsKeyUp => (Flags & KeyEventFlags.KeyUp) != 0;

    public bool IsUnicode => (Flags & KeyEventFlags.Unicode) != 0;

    public static KeyboardEvent UnicodeDown(char codeUnit) => new(0, codeUnit, KeyEventFlags.Unicode);

    public static KeyboardEvent UnicodeUp(char codeUnit) => new(0, codeUnit, KeyEventFlags.Unicode | KeyEventFlags.KeyUp);

    public static KeyboardEvent KeyDown(ushort virtualKey, ushort scanCode, bool extended = false) =>
        new(virtualKey, scanCode, extended ? KeyEventFlags.ExtendedKey : KeyEventFlags.None);

    public static KeyboardEvent KeyUp(ushort virtualKey, ushort scanCode, bool extended = false) =>
        new(virtualKey, scanCode, KeyEventFlags.KeyUp | (extended ? KeyEventFlags.ExtendedKey : KeyEventFlags.None));

    public override string ToString() => IsUnicode
        ? $"U+{ScanCode:X4} {(IsKeyUp ? "up" : "down")}"
        : $"VK 0x{VirtualKey:X2} {(IsKeyUp ? "up" : "down")}";
}
