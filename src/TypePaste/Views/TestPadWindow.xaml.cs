using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using TypePaste.Core.Input;
using TypePaste.Core.Text;
using TypePaste.Core.Typing;

namespace TypePaste.Views;

/// <summary>A local text box TypePaste types into to verify, character by character, that typing is exact.</summary>
public partial class TestPadWindow : Window
{
    public TestPadWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => App.Host?.Theme.ApplyWindowFrame(this);
    }

    /// <summary>Raised when the user clicks "Run again".</summary>
    public event EventHandler? RunAgainRequested;

    public nint Handle => new WindowInteropHelper(this).Handle;

    /// <summary>True while TypePaste is typing into the pad (the Run again button is disabled).</summary>
    public bool IsRunning
    {
        set => RunAgainButton.IsEnabled = !value;
    }

    /// <summary>Clears the pad and gives it keyboard focus so typed text lands in it.</summary>
    public void PrepareForTyping()
    {
        ResultBanner.Visibility = Visibility.Collapsed;
        TestBox.Clear();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        TestBox.Focus();
        Keyboard.Focus(TestBox);
    }

    /// <summary>True once the pad is the active window and its text box has keyboard focus.</summary>
    public bool IsReadyForInput => IsActive && TestBox.IsKeyboardFocused &&
        Native.NativeMethods.GetForegroundWindow() == Handle;

    /// <summary>
    /// Compares what arrived in the pad with what should have arrived and shows the verdict. Keystrokes that are still
    /// queued are given up to a few seconds to arrive first.
    /// </summary>
    internal async Task VerifyAsync(string source, TypingResult result)
    {
        var expected = ExpectedText(source, result.TypedLength);
        var actual = TestBox.Text;
        var lastLength = actual.Length;
        var quietSince = DateTime.UtcNow;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!string.Equals(actual, expected, StringComparison.Ordinal) && DateTime.UtcNow < deadline &&
               DateTime.UtcNow - quietSince < TimeSpan.FromMilliseconds(800))
        {
            await Task.Delay(50);
            actual = TestBox.Text;
            if (actual.Length != lastLength)
            {
                lastLength = actual.Length;
                quietSince = DateTime.UtcNow;
            }
        }

        var mismatch = FirstDifference(expected, actual);
        var characters = TextStatistics.CountCharacters(source, result.TypedLength);
        var culture = CultureInfo.CurrentCulture;
        var speed = result.Elapsed.TotalSeconds > 0.05 ? $" · {characters / result.Elapsed.TotalSeconds:N0} characters/s" : string.Empty;

        if (mismatch < 0 && result.Outcome == TypingOutcome.Completed)
        {
            Show("Perfect match", $"{characters.ToString("N0", culture)} characters typed exactly in {Format.Duration(result.Elapsed)}{speed}.", "SuccessSoftBrush", "SuccessBrush", "\uE73E");
        }
        else if (mismatch < 0)
        {
            Show($"Stopped — {Format.Outcome(result.Outcome)}", $"The {characters.ToString("N0", culture)} characters typed before stopping are exact.", "WarningSoftBrush", "WarningBrush", "\uE71A");
        }
        else
        {
            var expectedChar = mismatch < expected.Length ? Describe(expected[mismatch]) : "end of text";
            var actualChar = mismatch < actual.Length ? Describe(actual[mismatch]) : "end of text";
            Show("Mismatch", $"First difference at character {(mismatch + 1).ToString("N0", culture)}: expected {expectedChar}, got {actualChar}. " +
                "Try a slower speed or the keyboard-layout input method for this app.", "DangerSoftBrush", "DangerBrush", "\uE783");
        }
    }

    /// <summary>The text a standard text box contains after typing the first <paramref name="length"/> code units.</summary>
    internal static string ExpectedText(string source, int length)
    {
        var builder = new StringBuilder(length + 64);
        for (var i = 0; i < Math.Min(length, source.Length); i++)
        {
            var c = source[i];
            if (c == '\r')
            {
                builder.Append("\r\n");
                if (i + 1 < source.Length && source[i + 1] == '\n')
                {
                    i++;
                }
            }
            else if (c == '\n')
            {
                builder.Append("\r\n");
            }
            else if (!KeystrokePlanner.IsUntypeableControl(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    internal static int FirstDifference(string expected, string actual)
    {
        var length = Math.Min(expected.Length, actual.Length);
        for (var i = 0; i < length; i++)
        {
            if (expected[i] != actual[i])
            {
                return i;
            }
        }

        return expected.Length == actual.Length ? -1 : length;
    }

    private static string Describe(char c) => c switch
    {
        '\r' => "a line break",
        '\n' => "a line feed",
        '\t' => "a tab",
        ' ' => "a space",
        _ when char.IsControl(c) || char.IsSurrogate(c) || char.IsWhiteSpace(c) => $"U+{(int)c:X4}",
        _ => $"“{c}” (U+{(int)c:X4})",
    };

    private void Show(string title, string detail, string backgroundKey, string foregroundKey, string glyph)
    {
        ResultTitle.Text = title;
        ResultDetail.Text = detail;
        ResultGlyph.Text = glyph;
        ResultBanner.SetResourceReference(BackgroundProperty, backgroundKey);
        ResultGlyph.SetResourceReference(ForegroundProperty, foregroundKey);
        ResultTitle.SetResourceReference(ForegroundProperty, foregroundKey);
        ResultBanner.Visibility = Visibility.Visible;
    }

    private void RunAgain_Click(object sender, RoutedEventArgs e) => RunAgainRequested?.Invoke(this, EventArgs.Empty);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
