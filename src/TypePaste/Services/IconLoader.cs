using System.Buffers.Binary;
using System.Windows;
using static TypePaste.Native.NativeMethods;

namespace TypePaste.Services;

/// <summary>Creates native icon handles (for the tray) from the embedded TypePaste.ico.</summary>
internal static class IconLoader
{
    private static byte[]? _icoBytes;

    /// <summary>Returns an HICON of the requested size (caller destroys it), or 0 on failure.</summary>
    public static unsafe nint LoadAppIcon(int size)
    {
        try
        {
            var ico = _icoBytes ??= ReadIcoResource();
            var count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));

            // Choose the smallest image that is at least the requested size (or the largest available).
            var best = -1;
            var bestSize = 0;
            for (var i = 0; i < count; i++)
            {
                var entry = ico.AsSpan(6 + (16 * i), 16);
                var width = entry[0] == 0 ? 256 : entry[0];
                var better = best < 0
                    || (bestSize < size && width > bestSize)
                    || (width >= size && width < bestSize);
                if (better)
                {
                    best = i;
                    bestSize = width;
                }
            }

            if (best < 0)
            {
                return 0;
            }

            var chosen = ico.AsSpan(6 + (16 * best), 16);
            var length = BinaryPrimitives.ReadInt32LittleEndian(chosen[8..]);
            var offset = BinaryPrimitives.ReadInt32LittleEndian(chosen[12..]);
            fixed (byte* image = &ico[offset])
            {
                return CreateIconFromResourceEx(image, length, true, 0x00030000, size, size, 0);
            }
        }
        catch (Exception ex)
        {
            App.Log.Warn($"Could not load the tray icon: {ex.Message}");
            return 0;
        }
    }

    private static byte[] ReadIcoResource()
    {
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/TypePaste.ico", UriKind.Absolute))
            ?? throw new InvalidOperationException("TypePaste.ico resource is missing.");
        using var stream = info.Stream;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
