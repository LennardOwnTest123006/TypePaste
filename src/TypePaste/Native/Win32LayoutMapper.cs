using TypePaste.Core.Input;
using static TypePaste.Native.NativeMethods;

namespace TypePaste.Native;

/// <summary>
/// Maps characters to keys of a Windows keyboard layout. Every mapping is verified with <c>ToUnicodeEx</c> (without
/// touching the keyboard state), so dead keys and ambiguous keys are rejected and typed as Unicode instead.
/// </summary>
internal sealed unsafe class Win32LayoutMapper : IKeyboardLayoutMapper
{
    private const uint ToUnicodeDoNotChangeKeyboardState = 0x4;
    private const byte KeyDown = 0x80;

    private readonly nint _layout;
    private readonly Dictionary<char, LayoutKey?> _cache = new();

    public Win32LayoutMapper(nint layout)
    {
        _layout = layout;
    }

    public bool TryMap(char character, out LayoutKey key)
    {
        if (!_cache.TryGetValue(character, out var cached))
        {
            cached = Resolve(character);
            _cache[character] = cached;
        }

        key = cached.GetValueOrDefault();
        return cached.HasValue;
    }

    private LayoutKey? Resolve(char character)
    {
        var result = VkKeyScanExW(character, _layout);
        if (result == -1)
        {
            return null;
        }

        var vk = (ushort)(result & 0xFF);
        var shiftState = (result >> 8) & 0xFF;
        if ((shiftState & ~0x7) != 0 || vk is 0 or VirtualKeys.Return or VirtualKeys.Tab or VirtualKeys.Back or VirtualKeys.Escape)
        {
            return null;
        }

        var modifiers = LayoutModifiers.None;
        var state = stackalloc byte[256];
        if ((shiftState & 1) != 0)
        {
            modifiers |= LayoutModifiers.Shift;
            state[VirtualKeys.Shift] = KeyDown;
            state[VirtualKeys.LeftShift] = KeyDown;
        }

        if ((shiftState & 2) != 0)
        {
            modifiers |= LayoutModifiers.Control;
            state[VirtualKeys.Control] = KeyDown;
            state[VirtualKeys.LeftControl] = KeyDown;
        }

        if ((shiftState & 4) != 0)
        {
            modifiers |= LayoutModifiers.Alt;
            state[VirtualKeys.Menu] = KeyDown;
            state[VirtualKeys.LeftMenu] = KeyDown;
        }

        // Ctrl alone (without Alt) produces control characters, not text.
        if (modifiers.HasFlag(LayoutModifiers.Control) && !modifiers.HasFlag(LayoutModifiers.Alt))
        {
            return null;
        }

        var scan = (ushort)MapVirtualKeyExW(vk, MAPVK_VK_TO_VSC, _layout);
        if (scan == 0)
        {
            return null;
        }

        state[vk] = KeyDown;
        var output = stackalloc char[8];
        var produced = ToUnicodeEx(vk, scan, state, output, 8, ToUnicodeDoNotChangeKeyboardState, _layout);
        if (produced != 1 || output[0] != character)
        {
            // Dead key (-1), ligature (>1) or a different character: not safe to type with this key.
            return null;
        }

        return new LayoutKey(vk, scan, modifiers);
    }
}
