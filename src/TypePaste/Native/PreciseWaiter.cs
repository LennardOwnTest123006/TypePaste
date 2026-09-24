using System.Diagnostics;
using static TypePaste.Native.NativeMethods;

namespace TypePaste.Native;

/// <summary>
/// Sub-millisecond waits for the typing thread: a high-resolution waitable timer for the bulk of the wait and a short
/// spin for the remainder. Falls back to <c>Sleep</c> with a 1 ms system timer resolution on older Windows builds.
/// </summary>
internal sealed class PreciseWaiter : IDisposable
{
    private static readonly long SpinThreshold = Stopwatch.Frequency / 1000; // 1 ms
    private readonly nint _timer;
    private readonly bool _raisedTimerResolution;

    public PreciseWaiter()
    {
        _timer = CreateWaitableTimerExW(0, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
        if (_timer == 0)
        {
            _raisedTimerResolution = timeBeginPeriod(1) == 0;
        }
    }

    public void WaitUntil(long timestamp)
    {
        var remaining = timestamp - Stopwatch.GetTimestamp();
        if (remaining <= 0)
        {
            return;
        }

        if (remaining > SpinThreshold)
        {
            var sleepTicks = remaining - (SpinThreshold / 2);
            if (_timer != 0)
            {
                // Relative due time in 100 ns units (negative = relative).
                var due = -(long)(sleepTicks * 10_000_000.0 / Stopwatch.Frequency);
                if (due < 0 && SetWaitableTimer(_timer, ref due, 0, 0, 0, false))
                {
                    WaitForSingleObject(_timer, INFINITE);
                }
            }
            else
            {
                Thread.Sleep(TimeSpan.FromSeconds((double)sleepTicks / Stopwatch.Frequency));
            }
        }

        var spinner = default(SpinWait);
        while (Stopwatch.GetTimestamp() < timestamp)
        {
            spinner.SpinOnce(sleep1Threshold: -1);
        }
    }

    public void Dispose()
    {
        if (_timer != 0)
        {
            CloseHandle(_timer);
        }

        if (_raisedTimerResolution)
        {
            timeEndPeriod(1);
        }
    }
}
