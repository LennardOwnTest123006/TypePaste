using System.Text;

namespace TypePaste.E2E;

/// <summary>Texts used by the end-to-end tests.</summary>
internal static class Samples
{
    public const string Multiline =
        "Dear team,\r\n\r\nThis is the second paragraph.\nIt uses LF line endings, trailing spaces   \n\n\n" +
        "and a Windows line ending below:\r\n    indented with spaces\r\nLast line without a newline.";

    /// <summary>Every printable ASCII character plus common Latin-1 symbols.</summary>
    public static string Symbols
    {
        get
        {
            var builder = new StringBuilder("ASCII: ");
            for (var c = ' '; c <= '~'; c++)
            {
                builder.Append(c);
            }

            builder.Append("\nLatin-1: \u00A1\u00A2\u00A3\u00A4\u00A5\u00A6\u00A7\u00A8\u00A9\u00AA\u00AB\u00AC\u00AE\u00AF\u00B0\u00B1\u00B2\u00B3\u00B4\u00B5\u00B6\u00B7\u00B8\u00B9\u00BA\u00BB\u00BC\u00BD\u00BE\u00BF\u00D7\u00F7");
            builder.Append("\nQuotes: \u201Csmart\u201D \u2018single\u2019 \u2013 en dash \u2014 em dash \u2026 \u00AB\u00BB \u2039\u203A");
            builder.Append("\nCode: if (a != b && c <= d) { x[i] = y ?? z; } // #1 @user $5 50% ^_^ ~/path\\to\\file.txt");
            return builder.ToString();
        }
    }

    public const string Unicode =
        "Gr\u00FC\u00DFe aus K\u00F6ln \u2014 na\u00EFve caf\u00E9, fa\u00E7ade, \u0152uvre, \u00C5ngstr\u00F6m\n" +
        "\u0395\u03BB\u03BB\u03B7\u03BD\u03B9\u03BA\u03AC \u00B7 \u0420\u0443\u0441\u0441\u043A\u0438\u0439 \u00B7 \u4E2D\u6587\u5B57\u7B26 \u00B7 \u65E5\u672C\u8A9E\u306E\u30C6\u30AD\u30B9\u30C8 \u00B7 \uD55C\uAD6D\uC5B4\n" +
        "\u0627\u0644\u0639\u0631\u0628\u064A\u0629 \u00B7 \u05E2\u05D1\u05E8\u05D9\u05EA \u00B7 \u0939\u093F\u0928\u094D\u0926\u0940 \u00B7 \u0E44\u0E17\u0E22\n" +
        "Emoji: \uD83D\uDE00 \uD83C\uDF89 \uD83D\uDC4D\uD83C\uDFFD \uD83D\uDC68\u200D\uD83D\uDC69\u200D\uD83D\uDC67 \uD83C\uDDE9\uD83C\uDDEA \u2764\uFE0F \uD83D\uDE80\n" +
        "Math: \u2211 \u222B \u221A \u221E \u2260 \u2248 \u2264 \u2265 \u03C0 \u2206 \u00B1 \u2192 \u21D2 \u2208 \u2200\n" +
        "Combining: e\u0301 a\u0308 n\u0303 \u00B7 zero width:[\u200B] nbsp:[\u00A0] narrow nbsp:[\u202F] \u00B7 \uD835\uDC00\uD835\uDD38 end";

    public const string Tabs = "Name\tQty\tPrice\nApples\t3\t1.20\nPears\t12\t0.99\n\tindented with a tab\n\t\tdouble tab";

    /// <summary>A long mixed text of about <paramref name="length"/> UTF-16 code units.</summary>
    public static string Long(int length)
    {
        var words = new[]
        {
            "TypePaste", "types", "every", "character", "exactly", "as", "written,", "including", "punctuation!", "numbers",
            "12345", "and", "symbols", "(like", "#hashtags,", "@mentions", "&", "50%)", "\u2014", "caf\u00E9", "\u00FCber", "\u00DF",
            "\u4E2D\u6587", "\uD83D\uDE00", "\"quotes\"", "'apostrophes'", "{braces}", "[brackets]", "<angles>", "a/b\\c", "x=y+z;",
        };
        var random = new Random(42);
        var builder = new StringBuilder(length + 100);
        var lineLength = 0;
        while (builder.Length < length)
        {
            var word = words[random.Next(words.Length)];
            builder.Append(word);
            lineLength += word.Length;
            if (lineLength > 70)
            {
                builder.Append(random.Next(5) == 0 ? "\r\n\r\n" : "\r\n");
                lineLength = 0;
            }
            else
            {
                builder.Append(' ');
                lineLength++;
            }
        }

        return builder.ToString();
    }

    /// <summary>Text as a Win32/WinForms edit control stores it (CRLF line breaks, untypeable controls removed).</summary>
    public static string AsEditControl(string text) => NormalizeLineBreaks(text, "\r\n");

    /// <summary>Text as a browser textarea stores it (LF line breaks).</summary>
    public static string AsTextArea(string text) => NormalizeLineBreaks(text, "\n");

    public static string NormalizeLineBreaks(string text, string lineBreak)
    {
        var builder = new StringBuilder(text.Length + 16);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                builder.Append(lineBreak);
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
            }
            else if (c == '\n')
            {
                builder.Append(lineBreak);
            }
            else if (!(c < ' ' && c != '\t') && c != '\u007F')
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>FNV-1a over UTF-16 code units (the Edge test page computes the same hash in JavaScript).</summary>
    public static string Fnv1a(string text)
    {
        var hash = 0x811C9DC5u;
        foreach (var c in text)
        {
            hash ^= c;
            hash *= 0x01000193u;
        }

        return hash.ToString("x8");
    }
}
