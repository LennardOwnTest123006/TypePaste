using System.Globalization;
using TypePaste.Core.Typing;

namespace TypePaste;

/// <summary>User-facing formatting helpers.</summary>
internal static class Format
{
    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    public static string Count(int value, string singular, string plural) =>
        $"{value.ToString("N0", Culture)} {(value == 1 ? singular : plural)}";

    public static string Duration(TimeSpan span)
    {
        if (span.TotalSeconds < 10)
        {
            return $"{span.TotalSeconds.ToString("0.0#", Culture)} s";
        }

        if (span.TotalMinutes < 1)
        {
            return $"{span.TotalSeconds.ToString("0", Culture)} s";
        }

        if (span.TotalHours < 1)
        {
            return span.Seconds == 0 ? $"{(int)span.TotalMinutes} min" : $"{(int)span.TotalMinutes} min {span.Seconds} s";
        }

        return $"{(int)span.TotalHours} h {span.Minutes} min";
    }

    public static string Outcome(TypingOutcome outcome) => outcome switch
    {
        TypingOutcome.Completed => "completed",
        TypingOutcome.Cancelled => "stopped by you",
        TypingOutcome.FocusLost => "another window became active",
        TypingOutcome.TargetClosed => "the window was closed",
        TypingOutcome.InputBlocked => "Windows blocked the input",
        TypingOutcome.ModifierKeysHeld => "modifier keys were held down",
        _ => "an error occurred",
    };
}
