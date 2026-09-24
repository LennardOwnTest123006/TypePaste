using TypePaste.Core.Hotkeys;
using TypePaste.Core.Input;
using TypePaste.Core.Settings;
using TypePaste.Core.Text;
using TypePaste.Core.Typing;

namespace TypePaste.Core.Tests;

public class TextStatisticsTests
{
    [Fact]
    public void Empty_text_has_no_lines()
    {
        Assert.Equal(TextStatistics.Empty, TextStatistics.Compute(""));
        Assert.Equal(TextStatistics.Empty, TextStatistics.Compute(null));
    }

    [Fact]
    public void Counts_words_lines_and_characters()
    {
        var stats = TextStatistics.Compute("Hello world\r\nsecond  line\n\nlast");

        Assert.Equal(5, stats.Words);
        Assert.Equal(4, stats.Lines);
        Assert.Equal(3, stats.LineBreaks);
        Assert.Equal(30, stats.Characters); // CRLF counts as one character
        Assert.Equal(31, stats.Length);
    }

    [Fact]
    public void Emoji_and_combining_sequences_count_as_one_character()
    {
        var stats = TextStatistics.Compute("👍🏽👨\u200D👩\u200D👧 e\u0301");

        Assert.Equal(4, stats.Characters);
        Assert.Equal(2, stats.Words);
    }

    [Fact]
    public void Reports_untypeable_control_characters()
    {
        var stats = TextStatistics.Compute("a\0b\tc");

        Assert.Equal(1, stats.UntypeableControls);
        Assert.Equal(stats.Characters - 1, stats.Keystrokes);
    }

    [Fact]
    public void Counts_characters_of_a_prefix()
    {
        Assert.Equal(2, TextStatistics.CountCharacters("a😀b", 3));
        Assert.Equal(3, TextStatistics.CountCharacters("a😀b", 99));
        Assert.Equal(0, TextStatistics.CountCharacters("abc", -1));
    }
}

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("F6", HotkeyModifiers.None, VirtualKeys.F6)]
    [InlineData("Esc", HotkeyModifiers.None, VirtualKeys.Escape)]
    [InlineData("ctrl+shift+v", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x56)]
    [InlineData("Ctrl + Alt + F12", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x7B)]
    [InlineData("Win+NumPad+", HotkeyModifiers.Windows, 0x6B)]
    [InlineData("Ctrl+Key 0xE2", HotkeyModifiers.Control, 0xE2)]
    public void Parses_gestures(string text, HotkeyModifiers modifiers, ushort vk)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(new HotkeyGesture(modifiers, vk), gesture);
        Assert.True(HotkeyGesture.TryParse(gesture.ToString(), out var roundTrip));
        Assert.Equal(gesture, roundTrip);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+")]
    [InlineData("Hyper+F6")]
    [InlineData("F99")]
    public void Rejects_invalid_gestures(string text) => Assert.False(HotkeyGesture.TryParse(text, out _));

    [Fact]
    public void Formats_parts_for_keycaps()
    {
        Assert.Equal(["Ctrl", "Shift", "F6"], new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Shift, VirtualKeys.F6).Parts);
        Assert.Equal("F6", HotkeyGesture.DefaultStart.ToString());
        Assert.Equal("Esc", HotkeyGesture.DefaultStop.ToString());
    }

    [Theory]
    [InlineData("F6", true)]
    [InlineData("Pause", true)]
    [InlineData("A", false)]
    [InlineData("Shift+A", false)]
    [InlineData("Ctrl+A", true)]
    [InlineData("Space", false)]
    [InlineData("Alt+Space", true)]
    public void Validates_start_hotkeys(string text, bool valid)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(valid, gesture.Validate(allowPlainEscape: false, out var error));
        Assert.Equal(valid, error is null);
    }

    [Fact]
    public void Plain_escape_is_only_allowed_for_stopping()
    {
        Assert.True(HotkeyGesture.DefaultStop.Validate(allowPlainEscape: true, out _));
        Assert.False(HotkeyGesture.DefaultStop.Validate(allowPlainEscape: false, out _));
    }
}

public class AppSettingsTests
{
    [Fact]
    public void Defaults_match_the_product_behaviour()
    {
        var settings = new AppSettings();

        Assert.Equal("F6", settings.StartHotkey.ToString());
        Assert.Equal("Esc", settings.StopHotkey.ToString());
        Assert.Equal(SpeedMode.Instant, settings.Speed);
        Assert.Equal(InputMethod.Unicode, settings.InputMethod);
        Assert.True(settings.StopOnFocusChange);
    }

    [Fact]
    public void Round_trips_through_json()
    {
        var settings = new AppSettings
        {
            StartHotkey = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x54),
            Speed = SpeedMode.Custom,
            CustomDelayMs = 12.5,
            LineBreak = LineBreakStyle.ShiftEnter,
            Theme = ThemePreference.Dark,
            StartMinimizedToTray = true,
        };

        var json = SettingsSerializer.Serialize(settings);
        var copy = SettingsSerializer.Deserialize(json);

        Assert.Contains("\"Ctrl+Alt+T\"", json);
        Assert.Contains("\"ShiftEnter\"", json);
        Assert.Equal(json, SettingsSerializer.Serialize(copy));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"Speed\": \"Warp\"}")]
    public void Invalid_files_fall_back_to_defaults(string json)
    {
        var settings = SettingsSerializer.Deserialize(json);

        Assert.Equal(SpeedMode.Instant, settings.Speed);
        Assert.Equal(HotkeyGesture.DefaultStart, settings.StartHotkey);
    }

    [Fact]
    public void Out_of_range_values_are_clamped_and_bad_hotkeys_repaired()
    {
        var settings = SettingsSerializer.Deserialize(
            "{\"CustomDelayMs\": -5, \"LineBreakDelayMs\": 999999, \"StartCountdownSeconds\": 60, " +
            "\"StartHotkey\": \"A\", \"StopHotkey\": \"Space\", \"Speed\": 42}");

        Assert.Equal(0, settings.CustomDelayMs);
        Assert.Equal(TypingOptions.MaxLineBreakDelayMs, settings.LineBreakDelayMs);
        Assert.Equal(AppSettings.MaxCountdownSeconds, settings.StartCountdownSeconds);
        Assert.Equal(HotkeyGesture.DefaultStart, settings.StartHotkey);
        Assert.Equal(HotkeyGesture.DefaultStop, settings.StopHotkey);
        Assert.Equal(SpeedMode.Instant, settings.Speed);
    }

    [Fact]
    public void Identical_start_and_stop_hotkeys_are_reset()
    {
        var settings = new AppSettings
        {
            StartHotkey = new HotkeyGesture(HotkeyModifiers.Control, VirtualKeys.F6),
            StopHotkey = new HotkeyGesture(HotkeyModifiers.Control, VirtualKeys.F6),
        }.Normalize();

        Assert.NotEqual(settings.StartHotkey, settings.StopHotkey);
    }

    [Fact]
    public void Converts_to_typing_options()
    {
        var options = new AppSettings { Speed = SpeedMode.Steady, Tab = TabStyle.TabKey, StopOnFocusChange = false }.ToTypingOptions();

        Assert.Equal(SpeedMode.Steady, options.Speed);
        Assert.Equal(TabStyle.TabKey, options.Tab);
        Assert.False(options.StopOnFocusChange);
        Assert.Equal(TypingOptions.SteadyDelayMs, options.EffectiveDelayMs);
    }
}
