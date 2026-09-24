using TypePaste.Core.Input;
using TypePaste.Core.Typing;

namespace TypePaste.Core.Tests;

/// <summary>
/// Deterministic <see cref="ITypingPlatform"/> with a virtual clock (1 tick = 1 µs). Sent events are applied to a
/// <see cref="SimulatedTextField"/> so tests can check the exact text that would appear in the target.
/// </summary>
internal sealed class FakeTypingPlatform : ITypingPlatform
{
    public long Now { get; set; } = 1_000_000;

    public long TimestampFrequency => 1_000_000;

    public SimulatedTextField Field { get; } = new();

    public List<int> BatchSizes { get; } = [];

    /// <summary>Virtual time at which each batch was sent.</summary>
    public List<long> BatchTimes { get; } = [];

    public int EventsSent { get; private set; }

    public int IdleWaits { get; private set; }

    public IdleWaitResult IdleResult { get; set; } = IdleWaitResult.Idle;

    /// <summary>Optional result per idle-wait call (by call index); overrides <see cref="IdleResult"/>.</summary>
    public Func<int, IdleWaitResult>? IdleSequence { get; set; }

    /// <summary>Virtual time an idle wait takes.</summary>
    public long IdleWaitCost { get; set; } = 50;

    /// <summary>Number of <see cref="AreBlockingKeysDown"/> calls that report held keys (-1 = forever).</summary>
    public int BlockingKeyPolls { get; set; }

    public bool CapsLock { get; set; }

    public IKeyboardLayoutMapper? Layout { get; set; }

    /// <summary>Returns how many events of a batch the "system" accepts.</summary>
    public Func<int, int> Accept { get; set; } = count => count;

    public Func<TypingTarget, TargetState> TargetStateProvider { get; set; } = _ => TargetState.Active;

    public Func<bool> StopKeyProvider { get; set; } = () => false;

    /// <summary>Called after every batch; lets tests simulate things happening while typing.</summary>
    public Action<FakeTypingPlatform>? AfterSend { get; set; }

    public long GetTimestamp() => Now;

    public void WaitUntil(long timestamp)
    {
        // Advance at least one tick so polling loops always make progress.
        Now = Math.Max(Now + 1, timestamp);
    }

    public int SendKeyboardEvents(ReadOnlySpan<KeyboardEvent> events)
    {
        var accepted = Math.Clamp(Accept(events.Length), 0, events.Length);
        foreach (var e in events[..accepted])
        {
            Field.Apply(e);
        }

        BatchSizes.Add(events.Length);
        BatchTimes.Add(Now);
        EventsSent += accepted;
        Now += 5;
        AfterSend?.Invoke(this);
        return accepted;
    }

    public TargetState GetTargetState(TypingTarget target) => TargetStateProvider(target);

    public bool IsStopKeyDown() => StopKeyProvider();

    public bool AreBlockingKeysDown()
    {
        if (BlockingKeyPolls == 0)
        {
            return false;
        }

        if (BlockingKeyPolls > 0)
        {
            BlockingKeyPolls--;
        }

        return true;
    }

    public bool IsCapsLockOn() => CapsLock;

    public IdleWaitResult WaitForTargetIdle(TypingTarget target, TimeSpan timeout)
    {
        var result = IdleSequence?.Invoke(IdleWaits) ?? IdleResult;
        IdleWaits++;
        Now += result == IdleWaitResult.TimedOut ? (long)(timeout.TotalSeconds * TimestampFrequency) : IdleWaitCost;
        return result;
    }

    public IKeyboardLayoutMapper? CreateLayoutMapper(TypingTarget target) => Layout;
}
