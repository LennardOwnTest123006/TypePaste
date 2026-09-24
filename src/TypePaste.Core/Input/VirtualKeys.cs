using System.Collections.Frozen;

namespace TypePaste.Core.Input;

/// <summary>
/// Windows virtual-key codes and the scan codes TypePaste uses, plus friendly names for display and parsing.
/// </summary>
public static class VirtualKeys
{
    public const ushort Back = 0x08;
    public const ushort Tab = 0x09;
    public const ushort Return = 0x0D;
    public const ushort Shift = 0x10;
    public const ushort Control = 0x11;
    public const ushort Menu = 0x12;
    public const ushort Pause = 0x13;
    public const ushort Capital = 0x14;
    public const ushort Escape = 0x1B;
    public const ushort Space = 0x20;
    public const ushort PageUp = 0x21;
    public const ushort PageDown = 0x22;
    public const ushort End = 0x23;
    public const ushort Home = 0x24;
    public const ushort Left = 0x25;
    public const ushort Up = 0x26;
    public const ushort Right = 0x27;
    public const ushort Down = 0x28;
    public const ushort Insert = 0x2D;
    public const ushort Delete = 0x2E;
    public const ushort LeftWindows = 0x5B;
    public const ushort RightWindows = 0x5C;
    public const ushort NumPad0 = 0x60;
    public const ushort F1 = 0x70;
    public const ushort F6 = 0x75;
    public const ushort F24 = 0x87;
    public const ushort NumLock = 0x90;
    public const ushort Scroll = 0x91;
    public const ushort LeftShift = 0xA0;
    public const ushort RightShift = 0xA1;
    public const ushort LeftControl = 0xA2;
    public const ushort RightControl = 0xA3;
    public const ushort LeftMenu = 0xA4;
    public const ushort RightMenu = 0xA5;

    public const ushort ScanReturn = 0x1C;
    public const ushort ScanTab = 0x0F;
    public const ushort ScanLeftShift = 0x2A;
    public const ushort ScanLeftControl = 0x1D;
    public const ushort ScanLeftAlt = 0x38;

    private static readonly FrozenDictionary<ushort, string> Names = BuildNames();
    private static readonly FrozenDictionary<string, ushort> Codes =
        Names.ToFrozenDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>True for Shift, Ctrl, Alt and Windows keys (either side).</summary>
    public static bool IsModifier(ushort vk) => vk is Shift or Control or Menu or LeftWindows or RightWindows
        or LeftShift or RightShift or LeftControl or RightControl or LeftMenu or RightMenu;

    public static bool IsFunctionKey(ushort vk) => vk is >= F1 and <= F24;

    public static bool IsLetterOrDigit(ushort vk) => vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A;

    public static string GetName(ushort vk) => Names.TryGetValue(vk, out var name) ? name : $"Key 0x{vk:X2}";

    public static bool TryParse(string name, out ushort vk)
    {
        name = name.Trim();
        if (Codes.TryGetValue(name, out vk))
        {
            return true;
        }

        // Accept a few common aliases.
        switch (name.ToUpperInvariant())
        {
            case "ESCAPE":
                vk = Escape;
                return true;
            case "RETURN":
                vk = Return;
                return true;
            case "DEL":
                vk = Delete;
                return true;
            case "INS":
                vk = Insert;
                return true;
            case "PGUP":
                vk = PageUp;
                return true;
            case "PGDN":
                vk = PageDown;
                return true;
        }

        if (name.StartsWith("Key 0x", StringComparison.OrdinalIgnoreCase) &&
            ushort.TryParse(name.AsSpan(6), System.Globalization.NumberStyles.HexNumber, null, out vk) && vk is > 0 and < 0xFF)
        {
            return true;
        }

        vk = 0;
        return false;
    }

    private static FrozenDictionary<ushort, string> BuildNames()
    {
        var names = new Dictionary<ushort, string>
        {
            [Back] = "Backspace",
            [Tab] = "Tab",
            [Return] = "Enter",
            [Pause] = "Pause",
            [Capital] = "CapsLock",
            [Escape] = "Esc",
            [Space] = "Space",
            [PageUp] = "PageUp",
            [PageDown] = "PageDown",
            [End] = "End",
            [Home] = "Home",
            [Left] = "Left",
            [Up] = "Up",
            [Right] = "Right",
            [Down] = "Down",
            [Insert] = "Insert",
            [Delete] = "Delete",
            [NumLock] = "NumLock",
            [Scroll] = "ScrollLock",
            [0x6A] = "NumPad*",
            [0x6B] = "NumPad+",
            [0x6D] = "NumPad-",
            [0x6E] = "NumPad.",
            [0x6F] = "NumPad/",
            [0xBA] = ";",
            [0xBB] = "=",
            [0xBC] = ",",
            [0xBD] = "-",
            [0xBE] = ".",
            [0xBF] = "/",
            [0xC0] = "`",
            [0xDB] = "[",
            [0xDC] = "\\",
            [0xDD] = "]",
            [0xDE] = "'",
        };

        for (ushort i = 0; i < 24; i++)
        {
            names[(ushort)(F1 + i)] = $"F{i + 1}";
        }

        for (ushort i = 0; i < 10; i++)
        {
            names[(ushort)(0x30 + i)] = ((char)('0' + i)).ToString();
            names[(ushort)(NumPad0 + i)] = $"NumPad{i}";
        }

        for (ushort i = 0; i < 26; i++)
        {
            names[(ushort)(0x41 + i)] = ((char)('A' + i)).ToString();
        }

        return names.ToFrozenDictionary();
    }
}
