using System.Runtime.InteropServices;
using TypePaste.Core.Input;

namespace TypePaste.Core.Typing;

/// <summary>
/// Types text into the target window by injecting keyboard events. <see cref="Run"/> is synchronous and is meant to
/// run on a dedicated background thread; it can be stopped at any time through the cancellation token or the
/// physical stop key, and it stops on its own when the target window closes or loses focus.
/// </summary>
/// <remarks>
/// In <see cref="SpeedMode.Instant"/> mode keystrokes are sent in small batches, and after every batch the engine
/// waits until the target application has processed its input queue. This keeps typing as fast as the target can
/// accept it while never running far ahead of it, so stopping takes effect immediately and slow applications do
/// not drop characters. The paced modes send one keystroke at a time on a precise schedule.
/// </remarks>
public sealed class TypingEngine
{
    /// <summary>How long typing waits for the user to release modifier keys before giving up.</summary>
    public static readonly TimeSpan KeyReleaseTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Maximum time to wait for the target to process one instant-mode batch.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>After this many consecutive idle timeouts the engine switches to fixed pacing for the run.</summary>
    public const int MaxConsecutiveIdleTimeouts = 5;

    /// <summary>Pause between instant-mode batches when idle detection is unavailable.</summary>
    public static readonly TimeSpan FallbackBatchPause = TimeSpan.FromMilliseconds(1);

    /// <summary>Maximum characters per batch when idle detection is unavailable (caps the rate at a few thousand/s).</summary>
    public const int FallbackBatchSize = 8;

    /// <summary>If the paced schedule falls further behind than this, it is reset instead of catching up in a burst.</summary>
    public static readonly TimeSpan MaxScheduleLag = TimeSpan.FromMilliseconds(50);

    private static readonly TimeSpan KeyReleasePoll = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan WaitSlice = TimeSpan.FromMilliseconds(15);

    private readonly ITypingPlatform _platform;

    public TypingEngine(ITypingPlatform platform)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
    }

    /// <summary>Types <paramref name="text"/> into <paramref name="target"/>.</summary>
    public TypingResult Run(string text, TypingTarget target, TypingOptions options, TypingProgress progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(progress);

        var run = new TypingRun(this, text, target, options, progress, cancellationToken);
        try
        {
            return run.Execute();
        }
        catch (Exception ex)
        {
            return run.Finish(TypingOutcome.Failed, ex.Message);
        }
        finally
        {
            progress.SetPhase(TypingPhase.Finished);
        }
    }

    private long ToTicks(TimeSpan span) => (long)(span.TotalSeconds * _platform.TimestampFrequency);

    private long ToTicks(double milliseconds) => (long)(milliseconds / 1000.0 * _platform.TimestampFrequency);

    /// <summary>State of one typing run.</summary>
    private sealed class TypingRun
    {
        private readonly TypingEngine _engine;
        private readonly ITypingPlatform _platform;
        private readonly string _text;
        private readonly TypingTarget _target;
        private readonly TypingOptions _options;
        private readonly TypingProgress _progress;
        private readonly CancellationToken _cancellationToken;
        private readonly long _createdAt;

        private long _typingStartedAt;
        private int _index;
        private int _skipped;
        private bool _useIdleDetection = true;
        private int _consecutiveIdleTimeouts;

        public TypingRun(TypingEngine engine, string text, TypingTarget target, TypingOptions options, TypingProgress progress, CancellationToken cancellationToken)
        {
            _engine = engine;
            _platform = engine._platform;
            _text = text;
            _target = target;
            _options = options;
            _progress = progress;
            _cancellationToken = cancellationToken;
            _createdAt = _platform.GetTimestamp();
            _typingStartedAt = _createdAt;
        }

        public TypingResult Execute()
        {
            _progress.SetPhase(TypingPhase.WaitingForKeyRelease);
            if (WaitForKeyRelease() is { } early)
            {
                return Finish(early);
            }

            if (CheckInterruption() is { } interrupted)
            {
                return Finish(interrupted);
            }

            _progress.SetPhase(TypingPhase.Typing);
            _typingStartedAt = _platform.GetTimestamp();

            var layoutMode = _options.Method == InputMethod.KeyboardLayout;
            var planner = new KeystrokePlanner(
                _options,
                layoutMode ? _platform.CreateLayoutMapper(_target) : null,
                layoutMode && _platform.IsCapsLockOn());

            var instant = _options.Speed == SpeedMode.Instant;
            var delayTicks = _engine.ToTicks(_options.EffectiveDelayMs);
            var lineBreakTicks = _engine.ToTicks(_options.EffectiveLineBreakDelayMs);
            var maxLagTicks = _engine.ToTicks(MaxScheduleLag);
            var events = new List<KeyboardEvent>(_options.EffectiveInstantBatchSize * 8);
            var nextDue = _typingStartedAt;

            while (_index < _text.Length)
            {
                if (CheckInterruption() is { } stop)
                {
                    return Finish(stop);
                }

                events.Clear();
                var maxStrokesPerBatch = !instant ? 1
                    : _useIdleDetection ? _options.EffectiveInstantBatchSize
                    : Math.Min(_options.EffectiveInstantBatchSize, FallbackBatchSize);
                var batchStart = _index;
                var strokes = 0;
                var pauseForLineBreak = false;

                while (_index < _text.Length && strokes < maxStrokesPerBatch)
                {
                    var stroke = planner.Plan(_text, _index, events);
                    _index += stroke.Length;
                    if (stroke.Kind == StrokeKind.Skipped)
                    {
                        _skipped += stroke.Length;
                        continue;
                    }

                    strokes++;
                    if (stroke.Kind == StrokeKind.LineBreak && lineBreakTicks > 0)
                    {
                        pauseForLineBreak = true;
                        break;
                    }
                }

                if (events.Count > 0)
                {
                    var accepted = _platform.SendKeyboardEvents(CollectionsMarshal.AsSpan(events));
                    if (accepted != events.Count)
                    {
                        _index = batchStart;
                        return Finish(TypingOutcome.InputBlocked);
                    }
                }

                _progress.Report(_index);

                if (instant)
                {
                    if (strokes > 0)
                    {
                        PaceInstantBatch();
                    }

                    if (!pauseForLineBreak)
                    {
                        continue;
                    }

                    nextDue = _platform.GetTimestamp() + lineBreakTicks;
                }
                else
                {
                    nextDue += (delayTicks * strokes) + (pauseForLineBreak ? lineBreakTicks : 0);
                    var now = _platform.GetTimestamp();
                    if (now - nextDue > maxLagTicks)
                    {
                        // A hiccup (for example a busy system) delayed us; continue at the normal pace instead of
                        // sending the missed keystrokes in a burst.
                        nextDue = now;
                    }
                }

                if (_index < _text.Length && DelayUntil(nextDue) is { } interruptedWhileWaiting)
                {
                    return Finish(interruptedWhileWaiting);
                }
            }

            return Finish(TypingOutcome.Completed);
        }

        public TypingResult Finish(TypingOutcome outcome, string? error = null)
        {
            _progress.Report(_index);
            var elapsedTicks = Math.Max(0, _platform.GetTimestamp() - _typingStartedAt);
            var elapsed = TimeSpan.FromSeconds((double)elapsedTicks / _platform.TimestampFrequency);
            return new TypingResult(outcome, _index, _text.Length, _skipped, elapsed, error);
        }

        private TypingOutcome? WaitForKeyRelease()
        {
            var deadline = _createdAt + _engine.ToTicks(KeyReleaseTimeout);
            while (_platform.AreBlockingKeysDown())
            {
                if (_cancellationToken.IsCancellationRequested || _platform.IsStopKeyDown())
                {
                    return TypingOutcome.Cancelled;
                }

                if (_platform.GetTargetState(_target) == TargetState.Closed)
                {
                    return TypingOutcome.TargetClosed;
                }

                var now = _platform.GetTimestamp();
                if (now >= deadline)
                {
                    return TypingOutcome.ModifierKeysHeld;
                }

                _platform.WaitUntil(now + _engine.ToTicks(KeyReleasePoll));
            }

            return null;
        }

        private TypingOutcome? CheckInterruption()
        {
            if (_cancellationToken.IsCancellationRequested || _platform.IsStopKeyDown())
            {
                return TypingOutcome.Cancelled;
            }

            return _platform.GetTargetState(_target) switch
            {
                TargetState.Closed => TypingOutcome.TargetClosed,
                TargetState.FocusChanged when _options.StopOnFocusChange => TypingOutcome.FocusLost,
                _ => null,
            };
        }

        /// <summary>Waits until <paramref name="due"/>, checking for stop requests at least every few milliseconds.</summary>
        private TypingOutcome? DelayUntil(long due)
        {
            var slice = _engine.ToTicks(WaitSlice);
            while (true)
            {
                var now = _platform.GetTimestamp();
                if (now >= due)
                {
                    return null;
                }

                _platform.WaitUntil(Math.Min(due, now + slice));
                if (CheckInterruption() is { } stop)
                {
                    return stop;
                }
            }
        }

        private void PaceInstantBatch()
        {
            if (_useIdleDetection)
            {
                switch (_platform.WaitForTargetIdle(_target, IdleTimeout))
                {
                    case IdleWaitResult.Idle:
                        _consecutiveIdleTimeouts = 0;
                        return;

                    case IdleWaitResult.TimedOut:
                        // The target may simply be busy (heavy app), or it never idles in a message wait (for example
                        // a game loop). After several timeouts in a row, switch to fixed pacing for this run.
                        if (++_consecutiveIdleTimeouts >= MaxConsecutiveIdleTimeouts)
                        {
                            _useIdleDetection = false;
                        }

                        return;

                    default:
                        _useIdleDetection = false;
                        break;
                }
            }

            _platform.WaitUntil(_platform.GetTimestamp() + _engine.ToTicks(FallbackBatchPause));
        }
    }
}
