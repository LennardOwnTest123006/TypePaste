using TypePaste.Core.Input;
using TypePaste.Core.Typing;

namespace TypePaste.Core.Tests;

public class TypingEngineTests
{
    private static readonly TypingTarget Target = new(0x1234, 42, 7, "Target", "target.exe");

    private static TypingResult Run(FakeTypingPlatform platform, string text, TypingOptions? options = null, CancellationToken token = default)
    {
        var progress = new TypingProgress(text.Length);
        var result = new TypingEngine(platform).Run(text, Target, options ?? new TypingOptions(), progress, token);
        Assert.Equal(TypingPhase.Finished, progress.Phase);
        Assert.Equal(result.TypedLength, progress.TypedLength);
        return result;
    }

    private static string LongText(int length)
    {
        const string sample = "The quick brown fox jumps over the lazy dog — ÄÖÜ äöü ß € 😀\r\n\tIndented line with symbols !@#$%^&*()[]{}<>\n";
        var builder = new System.Text.StringBuilder(length + sample.Length);
        while (builder.Length < length)
        {
            builder.Append(sample);
        }

        return builder.ToString();
    }

    [Fact]
    public void Types_text_exactly_in_instant_mode()
    {
        var platform = new FakeTypingPlatform();
        var text = LongText(20_000);

        var result = Run(platform, text);

        Assert.Equal(TypingOutcome.Completed, result.Outcome);
        Assert.Equal(text.Length, result.TypedLength);
        Assert.Equal(SimulatedTextField.Expected(text), platform.Field.Text);
        Assert.Empty(platform.Field.Violations);
    }

    [Theory]
    [InlineData(SpeedMode.Fast)]
    [InlineData(SpeedMode.Steady)]
    [InlineData(SpeedMode.Custom)]
    public void Types_text_exactly_in_paced_modes(SpeedMode speed)
    {
        var platform = new FakeTypingPlatform();
        var text = LongText(2_000);

        var result = Run(platform, text, new TypingOptions { Speed = speed, CustomDelayMs = 0.5 });

        Assert.Equal(TypingOutcome.Completed, result.Outcome);
        Assert.Equal(SimulatedTextField.Expected(text), platform.Field.Text);
    }

    [Fact]
    public void Instant_mode_sends_bounded_batches_and_waits_for_the_target_after_each()
    {
        var platform = new FakeTypingPlatform();
        var text = new string('x', 1000);

        Run(platform, text, new TypingOptions { InstantBatchSize = 32 });

        Assert.All(platform.BatchSizes, size => Assert.True(size <= 64));
        Assert.Equal((int)Math.Ceiling(1000 / 32.0), platform.BatchSizes.Count);
        Assert.Equal(platform.BatchSizes.Count, platform.IdleWaits);
    }

    [Fact]
    public void Surrogate_pairs_are_never_split_between_batches()
    {
        var platform = new FakeTypingPlatform();
        var text = string.Concat(Enumerable.Repeat("a😀", 500));

        Run(platform, text, new TypingOptions { InstantBatchSize = 3 });

        // Each batch must contain complete characters: 2 events per BMP char, 4 per surrogate pair.
        Assert.All(platform.BatchSizes, size => Assert.Equal(0, size % 2));
        Assert.Equal(SimulatedTextField.Expected(text), platform.Field.Text);
    }

    [Fact]
    public void Paced_mode_sends_one_keystroke_per_delay()
    {
        var platform = new FakeTypingPlatform();

        Run(platform, "abcdef", new TypingOptions { Speed = SpeedMode.Custom, CustomDelayMs = 10 });

        Assert.Equal(6, platform.BatchSizes.Count);
        var gaps = platform.BatchTimes.Zip(platform.BatchTimes.Skip(1), (a, b) => b - a).ToList();
        Assert.All(gaps, gap => Assert.InRange(gap, 10_000, 10_100));
    }

    [Fact]
    public void Paced_mode_does_not_burst_after_falling_behind()
    {
        var platform = new FakeTypingPlatform();
        var sends = 0;
        platform.AfterSend = p =>
        {
            if (++sends == 3)
            {
                p.Now += 500_000; // the system stalls for half a second
            }
        };

        Run(platform, "abcdefgh", new TypingOptions { Speed = SpeedMode.Custom, CustomDelayMs = 10 });

        var gaps = platform.BatchTimes.Zip(platform.BatchTimes.Skip(1), (a, b) => b - a).Skip(3).ToList();
        Assert.All(gaps, gap => Assert.True(gap >= 9_000, $"gap {gap} µs is a burst"));
    }

    [Fact]
    public void Line_break_delay_pauses_after_each_line()
    {
        var platform = new FakeTypingPlatform();

        Run(platform, "ab\ncd\r\nef", new TypingOptions { LineBreakDelayMs = 200 });

        var elapsed = platform.BatchTimes[^1] - platform.BatchTimes[0];
        Assert.True(elapsed >= 400_000, $"elapsed {elapsed} µs");
        Assert.Equal("ab\ncd\nef", platform.Field.Text);
    }

    [Fact]
    public void Stops_immediately_when_cancelled()
    {
        var platform = new FakeTypingPlatform();
        using var cts = new CancellationTokenSource();
        var batches = 0;
        platform.AfterSend = _ =>
        {
            if (++batches == 3)
            {
                cts.Cancel();
            }
        };
        var text = new string('x', 10_000);

        var result = Run(platform, text, new TypingOptions { InstantBatchSize = 10 }, cts.Token);

        Assert.Equal(TypingOutcome.Cancelled, result.Outcome);
        Assert.Equal(30, result.TypedLength);
        Assert.Equal(new string('x', 30), platform.Field.Text);
    }

    [Fact]
    public void Stops_when_the_physical_stop_key_is_down()
    {
        var platform = new FakeTypingPlatform();
        var batches = 0;
        platform.AfterSend = _ => batches++;
        platform.StopKeyProvider = () => batches >= 5;

        var result = Run(platform, new string('x', 10_000), new TypingOptions { Speed = SpeedMode.Fast });

        Assert.Equal(TypingOutcome.Cancelled, result.Outcome);
        Assert.Equal(5, result.TypedLength);
    }

    [Fact]
    public void Stops_while_waiting_for_a_long_delay()
    {
        var platform = new FakeTypingPlatform();
        var stopAt = platform.Now + 50_000;
        platform.StopKeyProvider = () => platform.Now >= stopAt;

        var result = Run(platform, "abc", new TypingOptions { Speed = SpeedMode.Custom, CustomDelayMs = 1000 });

        Assert.Equal(TypingOutcome.Cancelled, result.Outcome);
        Assert.Equal(1, result.TypedLength);
        Assert.True(platform.Now - stopAt < 20_000, "stop request must be noticed within a few milliseconds");
    }

    [Fact]
    public void Stops_when_focus_moves_to_another_window()
    {
        var platform = new FakeTypingPlatform();
        var batches = 0;
        platform.AfterSend = _ => batches++;
        platform.TargetStateProvider = _ => batches >= 2 ? TargetState.FocusChanged : TargetState.Active;

        var result = Run(platform, new string('x', 1000), new TypingOptions { InstantBatchSize = 16 });

        Assert.Equal(TypingOutcome.FocusLost, result.Outcome);
        Assert.Equal(32, result.TypedLength);
    }

    [Fact]
    public void Keeps_typing_after_focus_change_when_configured()
    {
        var platform = new FakeTypingPlatform();
        platform.TargetStateProvider = _ => TargetState.FocusChanged;

        var result = Run(platform, "hello", new TypingOptions { StopOnFocusChange = false });

        Assert.Equal(TypingOutcome.Completed, result.Outcome);
    }

    [Fact]
    public void Stops_when_the_target_window_closes_even_if_focus_changes_are_allowed()
    {
        var platform = new FakeTypingPlatform();
        var batches = 0;
        platform.AfterSend = _ => batches++;
        platform.TargetStateProvider = _ => batches >= 1 ? TargetState.Closed : TargetState.Active;

        var result = Run(platform, new string('x', 100), new TypingOptions { InstantBatchSize = 10, StopOnFocusChange = false });

        Assert.Equal(TypingOutcome.TargetClosed, result.Outcome);
        Assert.Equal(10, result.TypedLength);
    }

    [Fact]
    public void Reports_blocked_input_without_counting_the_rejected_batch()
    {
        var platform = new FakeTypingPlatform();
        var batches = 0;
        platform.Accept = count => ++batches >= 3 ? 0 : count;

        var result = Run(platform, new string('x', 100), new TypingOptions { InstantBatchSize = 10 });

        Assert.Equal(TypingOutcome.InputBlocked, result.Outcome);
        Assert.Equal(20, result.TypedLength);
    }

    [Fact]
    public void Waits_for_modifier_keys_to_be_released_before_typing()
    {
        var platform = new FakeTypingPlatform { BlockingKeyPolls = 20 };
        var start = platform.Now;

        var result = Run(platform, "abc");

        Assert.Equal(TypingOutcome.Completed, result.Outcome);
        Assert.True(platform.BatchTimes[0] - start >= 190_000, "typing must not start while keys are held");
    }

    [Fact]
    public void Gives_up_when_modifier_keys_stay_down()
    {
        var platform = new FakeTypingPlatform { BlockingKeyPolls = -1 };

        var result = Run(platform, "abc");

        Assert.Equal(TypingOutcome.ModifierKeysHeld, result.Outcome);
        Assert.Equal(0, platform.EventsSent);
    }

    [Fact]
    public void Cancelling_while_waiting_for_key_release_sends_nothing()
    {
        var platform = new FakeTypingPlatform { BlockingKeyPolls = -1 };
        platform.StopKeyProvider = () => true;

        var result = Run(platform, "abc");

        Assert.Equal(TypingOutcome.Cancelled, result.Outcome);
        Assert.Equal(0, platform.EventsSent);
    }

    [Fact]
    public void Falls_back_to_fixed_pacing_when_idle_detection_is_unsupported()
    {
        var platform = new FakeTypingPlatform { IdleResult = IdleWaitResult.NotSupported };
        var text = new string('x', 320);

        var result = Run(platform, text, new TypingOptions { InstantBatchSize = 32 });

        Assert.Equal(TypingOutcome.Completed, result.Outcome);
        Assert.Equal(1, platform.IdleWaits);

        // First batch at full size, then smaller batches with a pause in between.
        Assert.Equal(64, platform.BatchSizes[0]);
        Assert.All(platform.BatchSizes.Skip(1), size => Assert.Equal(TypingEngine.FallbackBatchSize * 2, size));
        Assert.Equal(1 + ((320 - 32) / TypingEngine.FallbackBatchSize), platform.BatchSizes.Count);
    }

    [Fact]
    public void Stops_waiting_for_idle_after_repeated_timeouts()
    {
        var platform = new FakeTypingPlatform { IdleResult = IdleWaitResult.TimedOut };

        var result = Run(platform, new string('x', 32 * 20), new TypingOptions { InstantBatchSize = 32 });

        Assert.Equal(TypingOutcome.Completed, result.Outcome);
        Assert.Equal(TypingEngine.MaxConsecutiveIdleTimeouts, platform.IdleWaits);
    }

    [Fact]
    public void Keeps_waiting_for_a_busy_target_that_was_idle_before()
    {
        // Idle after the first batch, then busy for three waits, then idle again.
        var platform = new FakeTypingPlatform { IdleSequence = call => call is >= 1 and <= 3 ? IdleWaitResult.TimedOut : IdleWaitResult.Idle };
        var text = new string('x', 320);

        var result = Run(platform, text, new TypingOptions { InstantBatchSize = 32 });

        Assert.Equal(TypingOutcome.Completed, result.Outcome);
        Assert.Equal(10, platform.BatchSizes.Count);
        Assert.All(platform.BatchSizes, size => Assert.Equal(64, size));
        Assert.Equal(13, platform.IdleWaits);

        // The second batch waited until the target was idle again before the third one was sent.
        Assert.True(platform.BatchTimes[2] - platform.BatchTimes[1] >= 300_000);
    }

    [Fact]
    public void Continues_after_the_busy_wait_budget_when_a_target_stays_busy()
    {
        var platform = new FakeTypingPlatform { IdleSequence = call => call == 0 ? IdleWaitResult.Idle : IdleWaitResult.TimedOut };

        var result = Run(platform, new string('x', 96), new TypingOptions { InstantBatchSize = 32 });

        Assert.Equal(TypingOutcome.Completed, result.Outcome);
        Assert.All(platform.BatchSizes, size => Assert.Equal(64, size));
        var gap = platform.BatchTimes[2] - platform.BatchTimes[1];
        Assert.InRange(gap, 2_000_000, 2_200_000);
    }

    [Fact]
    public void Stop_request_is_honoured_while_waiting_for_a_busy_target()
    {
        var platform = new FakeTypingPlatform { IdleSequence = call => call == 0 ? IdleWaitResult.Idle : IdleWaitResult.TimedOut };
        var stopAt = platform.Now + 400_000;
        platform.StopKeyProvider = () => platform.Now >= stopAt;

        var result = Run(platform, new string('x', 320), new TypingOptions { InstantBatchSize = 32 });

        Assert.Equal(TypingOutcome.Cancelled, result.Outcome);
        Assert.Equal(64, result.TypedLength);
        Assert.True(platform.Now - stopAt < 200_000, "the stop request must end the busy wait promptly");
    }

    [Fact]
    public void Skips_untypeable_control_characters_and_reports_them()
    {
        var platform = new FakeTypingPlatform();

        var result = Run(platform, "a\0b\u0007c");

        Assert.Equal(TypingOutcome.Completed, result.Outcome);
        Assert.Equal(2, result.SkippedControlCharacters);
        Assert.Equal("abc", platform.Field.Text);
    }

    [Fact]
    public void Shift_enter_line_breaks_are_used_when_configured()
    {
        var platform = new FakeTypingPlatform();

        Run(platform, "a\nb", new TypingOptions { LineBreak = LineBreakStyle.ShiftEnter });

        Assert.Equal("a\nb", platform.Field.Text);
    }

    [Fact]
    public void Keyboard_layout_mode_types_exact_text()
    {
        var layout = new FakeLayout();
        var platform = new FakeTypingPlatform { Layout = layout };
        layout.Register(platform.Field);
        const string text = "Hello World! mail@example — ok 😀\nDone";

        var result = Run(platform, text, new TypingOptions { Method = InputMethod.KeyboardLayout });

        Assert.Equal(TypingOutcome.Completed, result.Outcome);
        Assert.Empty(platform.Field.Violations);
        Assert.Equal(SimulatedTextField.Expected(text), platform.Field.Text);
    }

    [Fact]
    public void Empty_text_completes_without_sending()
    {
        var platform = new FakeTypingPlatform();

        var result = Run(platform, string.Empty);

        Assert.Equal(TypingOutcome.Completed, result.Outcome);
        Assert.Equal(0, platform.EventsSent);
    }

    [Fact]
    public void Platform_exceptions_are_reported_as_failure()
    {
        var platform = new FakeTypingPlatform { Accept = _ => throw new InvalidOperationException("boom") };

        var result = Run(platform, "abc");

        Assert.Equal(TypingOutcome.Failed, result.Outcome);
        Assert.Equal("boom", result.ErrorMessage);
    }

    [Fact]
    public void Estimates_duration_for_paced_modes_only()
    {
        Assert.Null(new TypingOptions().EstimateDuration(1000, 10));
        Assert.Equal(TimeSpan.FromMilliseconds(2000 + 100), new TypingOptions { Speed = SpeedMode.Fast, LineBreakDelayMs = 10 }.EstimateDuration(1000, 10));
        Assert.Equal(TypingOptions.MaxCustomDelayMs, new TypingOptions { Speed = SpeedMode.Custom, CustomDelayMs = 99_999 }.EffectiveDelayMs);
        Assert.Equal(0, new TypingOptions { Speed = SpeedMode.Custom, CustomDelayMs = double.NaN }.EffectiveDelayMs);
    }
}
