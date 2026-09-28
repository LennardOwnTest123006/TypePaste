using System.Globalization;
using TypePaste.Core.Input;

namespace TypePaste.Core.Text;

/// <summary>Counts shown under the editor.</summary>
/// <param name="Characters">User-perceived characters (grapheme clusters; an emoji or CRLF counts once).</param>
/// <param name="Words">Runs of non-whitespace characters.</param>
/// <param name="Lines">Number of lines (0 for empty text).</param>
/// <param name="LineBreaks">Number of line breaks (CRLF, CR or LF).</param>
/// <param name="Length">Length in UTF-16 code units.</param>
/// <param name="UntypeableControls">Control characters that cannot be typed and will be skipped.</param>
public readonly record struct TextStatistics(int Characters, int Words, int Lines, int LineBreaks, int Length, int UntypeableControls)
{
    public static TextStatistics Empty { get; } = new(0, 0, 0, 0, 0, 0);

    /// <summary>Keystrokes that will be sent (one per character, including line breaks).</summary>
    public int Keystrokes => Characters - UntypeableControls;

    public static TextStatistics Compute(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Empty;
        }

        var words = 0;
        var lineBreaks = 0;
        var inWord = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                lineBreaks++;
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                inWord = false;
                continue;
            }

            if (c == '\n')
            {
                lineBreaks++;
                inWord = false;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (!inWord && !KeystrokePlanner.IsUntypeableControl(c))
            {
                inWord = true;
                words++;
            }
        }

        return new TextStatistics(
            CountGraphemes(text),
            words,
            lineBreaks + 1,
            lineBreaks,
            text.Length,
            KeystrokePlanner.CountUntypeableControls(text));
    }

    /// <summary>Counts user-perceived characters in the first <paramref name="length"/> code units of the text.</summary>
    public static int CountCharacters(string text, int length)
    {
        ArgumentNullException.ThrowIfNull(text);
        length = Math.Clamp(length, 0, text.Length);
        return CountGraphemes(text.AsSpan(0, length));
    }

    private static int CountGraphemes(ReadOnlySpan<char> text)
    {
        var count = 0;
        while (!text.IsEmpty)
        {
            var next = StringInfo.GetNextTextElementLength(text);
            if (next <= 0)
            {
                next = 1;
            }

            text = text[next..];
            count++;
        }

        return count;
    }
}
