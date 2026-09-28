namespace TypePaste.Core.Typing;

public enum TypingOutcome
{
    Completed,

    /// <summary>Stopped by the user (stop hotkey, button or tray).</summary>
    Cancelled,

    /// <summary>Another window became active.</summary>
    FocusLost,

    /// <summary>The target window was closed.</summary>
    TargetClosed,

    /// <summary>Windows rejected the simulated input (for example a secure desktop or a higher-privileged app).</summary>
    InputBlocked,

    /// <summary>Modifier keys were still held down, so typing did not start.</summary>
    ModifierKeysHeld,

    Failed,
}

/// <summary>Outcome of one typing run.</summary>
public sealed record TypingResult(
    TypingOutcome Outcome,
    int TypedLength,
    int TotalLength,
    int SkippedControlCharacters,
    TimeSpan Elapsed,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Outcome == TypingOutcome.Completed;

    /// <summary>Average typing speed in UTF-16 code units per second.</summary>
    public double CharactersPerSecond => Elapsed.TotalSeconds > 0 ? TypedLength / Elapsed.TotalSeconds : 0;
}
