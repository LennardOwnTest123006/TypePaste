namespace TypePaste.Core.Typing;

/// <summary>How fast TypePaste types.</summary>
public enum SpeedMode
{
    /// <summary>As fast as the target application accepts input (adaptive pacing, batched keystrokes).</summary>
    Instant,

    /// <summary>One keystroke every 2 ms (about 500 characters per second).</summary>
    Fast,

    /// <summary>One keystroke every 15 ms (about 65 characters per second). Good for picky apps and games.</summary>
    Steady,

    /// <summary>User-defined delay between characters.</summary>
    Custom,
}

/// <summary>Which key combination is used for line breaks.</summary>
public enum LineBreakStyle
{
    /// <summary>Press Enter for every line break.</summary>
    Enter,

    /// <summary>Press Shift+Enter (keeps chat apps from sending the message on every line).</summary>
    ShiftEnter,
}

/// <summary>How tab characters are typed.</summary>
public enum TabStyle
{
    /// <summary>Type a literal tab character (keeps the text exact; focus does not move).</summary>
    Character,

    /// <summary>Press the Tab key (may move focus in forms).</summary>
    TabKey,
}

/// <summary>How characters are turned into keyboard input.</summary>
public enum InputMethod
{
    /// <summary>Unicode keyboard packets: exact, layout independent, supports every character.</summary>
    Unicode,

    /// <summary>
    /// Physical keys of the current keyboard layout where possible (compatibility mode for apps that ignore
    /// Unicode packets, e.g. some games and remote desktop clients). Falls back to Unicode for other characters.
    /// </summary>
    KeyboardLayout,
}

/// <summary>Immutable options for one typing run.</summary>
public sealed record TypingOptions
{
    public const double FastDelayMs = 2;
    public const double SteadyDelayMs = 15;
    public const double MaxCustomDelayMs = 1000;
    public const int MaxLineBreakDelayMs = 5000;
    public const int DefaultInstantBatchSize = 32;

    public SpeedMode Speed { get; init; } = SpeedMode.Instant;

    /// <summary>Delay between characters in milliseconds when <see cref="Speed"/> is <see cref="SpeedMode.Custom"/>.</summary>
    public double CustomDelayMs { get; init; } = 5;

    /// <summary>Additional pause after each line break, in milliseconds.</summary>
    public int LineBreakDelayMs { get; init; }

    public LineBreakStyle LineBreak { get; init; } = LineBreakStyle.Enter;

    public TabStyle Tab { get; init; } = TabStyle.Character;

    public InputMethod Method { get; init; } = InputMethod.Unicode;

    /// <summary>Stop typing as soon as another window becomes active.</summary>
    public bool StopOnFocusChange { get; init; } = true;

    /// <summary>Maximum number of characters sent per batch in <see cref="SpeedMode.Instant"/> mode.</summary>
    public int InstantBatchSize { get; init; } = DefaultInstantBatchSize;

    /// <summary>The delay between two characters in milliseconds (0 for instant mode).</summary>
    public double EffectiveDelayMs => Speed switch
    {
        SpeedMode.Instant => 0,
        SpeedMode.Fast => FastDelayMs,
        SpeedMode.Steady => SteadyDelayMs,
        _ => Math.Clamp(double.IsFinite(CustomDelayMs) ? CustomDelayMs : 0, 0, MaxCustomDelayMs),
    };

    public int EffectiveLineBreakDelayMs => Math.Clamp(LineBreakDelayMs, 0, MaxLineBreakDelayMs);

    public int EffectiveInstantBatchSize => Math.Clamp(InstantBatchSize, 1, 512);

    /// <summary>
    /// Estimated duration for typing the given number of characters and line breaks,
    /// or null for instant mode (which depends on how fast the target app is).
    /// </summary>
    public TimeSpan? EstimateDuration(int characters, int lineBreaks)
    {
        if (Speed == SpeedMode.Instant)
        {
            return null;
        }

        var ms = (characters * EffectiveDelayMs) + (lineBreaks * (double)EffectiveLineBreakDelayMs);
        return TimeSpan.FromMilliseconds(ms);
    }
}
