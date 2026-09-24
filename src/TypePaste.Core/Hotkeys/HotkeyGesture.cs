using TypePaste.Core.Input;

namespace TypePaste.Core.Hotkeys;

/// <summary>Modifier flags of a global hotkey. Values match the Win32 <c>MOD_*</c> constants.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Windows = 0x8,
}

/// <summary>A global hotkey such as <c>F6</c> or <c>Ctrl+Shift+V</c>.</summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, ushort VirtualKey)
{
    public static HotkeyGesture DefaultStart { get; } = new(HotkeyModifiers.None, VirtualKeys.F6);

    public static HotkeyGesture DefaultStop { get; } = new(HotkeyModifiers.None, VirtualKeys.Escape);

    public bool IsEmpty => VirtualKey == 0;

    public override string ToString() => IsEmpty ? "None" : string.Join("+", Parts);

    /// <summary>The individual parts for keycap-style display, e.g. ["Ctrl", "Shift", "V"].</summary>
    public IReadOnlyList<string> Parts
    {
        get
        {
            var parts = new List<string>(4);
            if ((Modifiers & HotkeyModifiers.Control) != 0)
            {
                parts.Add("Ctrl");
            }

            if ((Modifiers & HotkeyModifiers.Alt) != 0)
            {
                parts.Add("Alt");
            }

            if ((Modifiers & HotkeyModifiers.Shift) != 0)
            {
                parts.Add("Shift");
            }

            if ((Modifiers & HotkeyModifiers.Windows) != 0)
            {
                parts.Add("Win");
            }

            parts.Add(IsEmpty ? "None" : VirtualKeys.GetName(VirtualKey));
            return parts;
        }
    }

    public static bool TryParse(string? value, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        var tokens = SplitTokens(value.Trim());
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            switch (tokens[i].ToUpperInvariant())
            {
                case "CTRL" or "CONTROL":
                    modifiers |= HotkeyModifiers.Control;
                    break;
                case "ALT":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "SHIFT":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "WIN" or "WINDOWS":
                    modifiers |= HotkeyModifiers.Windows;
                    break;
                default:
                    return false;
            }
        }

        if (!VirtualKeys.TryParse(tokens[^1], out var vk) || VirtualKeys.IsModifier(vk))
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, vk);
        return true;
    }

    private static bool IsModifierName(string name) =>
        name.ToUpperInvariant() is "CTRL" or "CONTROL" or "ALT" or "SHIFT" or "WIN" or "WINDOWS";

    /// <summary>Splits "Ctrl+Shift+NumPad+" into modifiers and the key name (which may itself contain '+').</summary>
    private static string[] SplitTokens(string value)
    {
        var tokens = new List<string>();
        var rest = value;
        while (true)
        {
            var plus = rest.IndexOf('+');
            if (plus <= 0 || plus == rest.Length - 1 || !IsModifierName(rest[..plus].Trim()))
            {
                break;
            }

            tokens.Add(rest[..plus].Trim());
            rest = rest[(plus + 1)..];
        }

        tokens.Add(rest.Trim());
        return [.. tokens];
    }

    /// <summary>
    /// Checks whether the gesture is safe to use as a global hotkey. Keys that are needed for normal typing
    /// (letters, digits, Space, Enter, ...) require at least one modifier other than Shift.
    /// </summary>
    public bool Validate(bool allowPlainEscape, out string? error)
    {
        error = null;
        if (IsEmpty || VirtualKeys.IsModifier(VirtualKey))
        {
            error = "Choose a key (modifier keys alone cannot be used).";
            return false;
        }

        var hasRealModifier = (Modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Windows)) != 0;
        if (VirtualKey == VirtualKeys.Escape && Modifiers == HotkeyModifiers.None)
        {
            if (!allowPlainEscape)
            {
                error = "Esc on its own is reserved for stopping. Add a modifier such as Ctrl.";
                return false;
            }

            return true;
        }

        var isSafeAlone = VirtualKeys.IsFunctionKey(VirtualKey) || VirtualKey is VirtualKeys.Pause or VirtualKeys.Scroll or VirtualKeys.Insert;
        if (!isSafeAlone && !hasRealModifier)
        {
            error = $"{VirtualKeys.GetName(VirtualKey)} is needed for normal typing. Use a function key (F1–F24) or add Ctrl, Alt or Win.";
            return false;
        }

        return true;
    }
}
