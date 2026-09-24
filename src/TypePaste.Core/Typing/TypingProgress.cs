namespace TypePaste.Core.Typing;

/// <summary>
/// Thread-safe progress of a typing run. The engine writes it from its worker thread; the UI polls it.
/// </summary>
public sealed class TypingProgress
{
    private int _typedLength;
    private int _phase;

    public TypingProgress(int totalLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalLength);
        TotalLength = totalLength;
    }

    /// <summary>Total length of the text in UTF-16 code units.</summary>
    public int TotalLength { get; }

    /// <summary>Number of UTF-16 code units of the text that have been sent so far.</summary>
    public int TypedLength => Volatile.Read(ref _typedLength);

    public TypingPhase Phase => (TypingPhase)Volatile.Read(ref _phase);

    /// <summary>Progress between 0 and 1.</summary>
    public double Fraction => TotalLength == 0 ? 1 : Math.Clamp((double)TypedLength / TotalLength, 0, 1);

    internal void SetPhase(TypingPhase phase) => Volatile.Write(ref _phase, (int)phase);

    internal void Report(int typedLength) => Volatile.Write(ref _typedLength, typedLength);
}

public enum TypingPhase
{
    /// <summary>Waiting for the user to release modifier keys.</summary>
    WaitingForKeyRelease,

    Typing,

    Finished,
}
