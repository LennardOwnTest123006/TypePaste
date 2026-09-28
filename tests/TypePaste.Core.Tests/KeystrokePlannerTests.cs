using TypePaste.Core.Input;
using TypePaste.Core.Typing;

namespace TypePaste.Core.Tests;

public class KeystrokePlannerTests
{
    private static (List<KeyboardEvent> Events, List<Stroke> Strokes) PlanAll(string text, TypingOptions? options = null, IKeyboardLayoutMapper? layout = null, bool capsLock = false)
    {
        var planner = new KeystrokePlanner(options ?? new TypingOptions(), layout, capsLock);
        var events = new List<KeyboardEvent>();
        var strokes = new List<Stroke>();
        for (var i = 0; i < text.Length;)
        {
            var stroke = planner.Plan(text, i, events);
            Assert.True(stroke.Length > 0);
            strokes.Add(stroke);
            i += stroke.Length;
        }

        return (events, strokes);
    }

    [Fact]
    public void Plain_character_is_a_unicode_key_down_and_up()
    {
        var (events, strokes) = PlanAll("a");

        Assert.Equal([KeyboardEvent.UnicodeDown('a'), KeyboardEvent.UnicodeUp('a')], events);
        Assert.Equal(StrokeKind.Character, Assert.Single(strokes).Kind);
    }

    [Theory]
    [InlineData("\r\n", 2)]
    [InlineData("\n", 1)]
    [InlineData("\r", 1)]
    public void Every_line_break_style_becomes_one_enter_press(string lineBreak, int length)
    {
        var (events, strokes) = PlanAll(lineBreak);

        Assert.Equal(
            [KeyboardEvent.KeyDown(VirtualKeys.Return, VirtualKeys.ScanReturn), KeyboardEvent.KeyUp(VirtualKeys.Return, VirtualKeys.ScanReturn)],
            events);
        var stroke = Assert.Single(strokes);
        Assert.Equal(StrokeKind.LineBreak, stroke.Kind);
        Assert.Equal(length, stroke.Length);
    }

    [Fact]
    public void Lf_followed_by_cr_is_two_line_breaks()
    {
        var (_, strokes) = PlanAll("\n\r");

        Assert.Equal(2, strokes.Count);
        Assert.All(strokes, s => Assert.Equal(StrokeKind.LineBreak, s.Kind));
    }

    [Fact]
    public void Shift_enter_style_wraps_enter_in_shift()
    {
        var (events, _) = PlanAll("\n", new TypingOptions { LineBreak = LineBreakStyle.ShiftEnter });

        Assert.Equal(
            [
                KeyboardEvent.KeyDown(VirtualKeys.LeftShift, VirtualKeys.ScanLeftShift),
                KeyboardEvent.KeyDown(VirtualKeys.Return, VirtualKeys.ScanReturn),
                KeyboardEvent.KeyUp(VirtualKeys.Return, VirtualKeys.ScanReturn),
                KeyboardEvent.KeyUp(VirtualKeys.LeftShift, VirtualKeys.ScanLeftShift),
            ],
            events);
    }

    [Fact]
    public void Tab_is_a_literal_character_by_default()
    {
        var (events, strokes) = PlanAll("\t");

        Assert.Equal([KeyboardEvent.UnicodeDown('\t'), KeyboardEvent.UnicodeUp('\t')], events);
        Assert.Equal(StrokeKind.Tab, Assert.Single(strokes).Kind);
    }

    [Fact]
    public void Tab_can_press_the_tab_key()
    {
        var (events, _) = PlanAll("\t", new TypingOptions { Tab = TabStyle.TabKey });

        Assert.Equal(
            [KeyboardEvent.KeyDown(VirtualKeys.Tab, VirtualKeys.ScanTab), KeyboardEvent.KeyUp(VirtualKeys.Tab, VirtualKeys.ScanTab)],
            events);
    }

    [Fact]
    public void Surrogate_pair_is_planned_as_one_stroke()
    {
        const string emoji = "😀";
        var (events, strokes) = PlanAll(emoji);

        var stroke = Assert.Single(strokes);
        Assert.Equal(2, stroke.Length);
        Assert.Equal(
            [
                KeyboardEvent.UnicodeDown(emoji[0]),
                KeyboardEvent.UnicodeUp(emoji[0]),
                KeyboardEvent.UnicodeDown(emoji[1]),
                KeyboardEvent.UnicodeUp(emoji[1]),
            ],
            events);
    }

    [Fact]
    public void Lone_surrogate_is_still_sent_unchanged()
    {
        var (events, strokes) = PlanAll("\uD83D");

        Assert.Equal(StrokeKind.Character, Assert.Single(strokes).Kind);
        Assert.Equal([KeyboardEvent.UnicodeDown('\uD83D'), KeyboardEvent.UnicodeUp('\uD83D')], events);
    }

    [Theory]
    [InlineData('\0')]
    [InlineData('\a')]
    [InlineData('\b')]
    [InlineData('\u001B')]
    [InlineData('\u007F')]
    public void Untypeable_control_characters_are_skipped(char c)
    {
        var (events, strokes) = PlanAll(c.ToString());

        Assert.Empty(events);
        Assert.Equal(StrokeKind.Skipped, Assert.Single(strokes).Kind);
        Assert.True(KeystrokePlanner.IsUntypeableControl(c));
    }

    [Theory]
    [InlineData('\t')]
    [InlineData('\r')]
    [InlineData('\n')]
    [InlineData(' ')]
    [InlineData('\u00A0')]
    [InlineData('\u2028')]
    public void Whitespace_and_line_breaks_are_typeable(char c) => Assert.False(KeystrokePlanner.IsUntypeableControl(c));

    [Fact]
    public void Complex_text_round_trips_exactly()
    {
        const string text = "Hello, World!\r\nLine 2\twith tab\nLine 3 — ünïcödé ß € £ ¥ © ® ™ ½ ≠ ≤ ∞ π Ω\r" +
            "Symbols: ~`!@#$%^&*()_+-={}[]|\\:;\"'<>,.?/\n" +
            "中文 日本語 한국어 العربية עברית Ελληνικά Русский हिन्दी 👍🏽 👨\u200D👩\u200D👧\u200D👦 🇩🇪 e\u0301 \uFEFF\u200B end";
        var (events, _) = PlanAll(text);
        var field = new SimulatedTextField();

        field.Apply(events);

        Assert.Empty(field.Violations);
        Assert.Equal(SimulatedTextField.Expected(text), field.Text);
    }

    [Fact]
    public void Keyboard_layout_mode_uses_physical_keys_with_modifiers()
    {
        var layout = new FakeLayout();
        var (events, _) = PlanAll("aB@", new TypingOptions { Method = InputMethod.KeyboardLayout }, layout);

        Assert.DoesNotContain(events, e => e.IsUnicode);
        Assert.Equal(
            [
                KeyboardEvent.KeyDown(0x41, 0x10),
                KeyboardEvent.KeyUp(0x41, 0x10),
                KeyboardEvent.KeyDown(VirtualKeys.LeftShift, VirtualKeys.ScanLeftShift),
                KeyboardEvent.KeyDown(0x42, 0x10),
                KeyboardEvent.KeyUp(0x42, 0x10),
                KeyboardEvent.KeyUp(VirtualKeys.LeftShift, VirtualKeys.ScanLeftShift),
                KeyboardEvent.KeyDown(VirtualKeys.LeftControl, VirtualKeys.ScanLeftControl),
                KeyboardEvent.KeyDown(VirtualKeys.LeftMenu, VirtualKeys.ScanLeftAlt),
                KeyboardEvent.KeyDown(0x51, 0x10),
                KeyboardEvent.KeyUp(0x51, 0x10),
                KeyboardEvent.KeyUp(VirtualKeys.LeftMenu, VirtualKeys.ScanLeftAlt),
                KeyboardEvent.KeyUp(VirtualKeys.LeftControl, VirtualKeys.ScanLeftControl),
            ],
            events);
    }

    [Fact]
    public void Keyboard_layout_mode_falls_back_to_unicode_for_unmapped_characters()
    {
        var layout = new FakeLayout();
        var field = new SimulatedTextField();
        layout.Register(field);
        const string text = "Hi there! é 😀 ok@home\nnext";

        var (events, _) = PlanAll(text, new TypingOptions { Method = InputMethod.KeyboardLayout }, layout);
        field.Apply(events);

        Assert.Empty(field.Violations);
        Assert.Equal(SimulatedTextField.Expected(text), field.Text);
        Assert.Contains(events, e => e.IsUnicode && e.ScanCode == 'é');
    }

    [Fact]
    public void Keyboard_layout_mode_types_letters_as_unicode_when_caps_lock_is_on()
    {
        var (events, _) = PlanAll("aA!", new TypingOptions { Method = InputMethod.KeyboardLayout }, new FakeLayout(), capsLock: true);

        Assert.Equal(KeyboardEvent.UnicodeDown('a'), events[0]);
        Assert.Equal(KeyboardEvent.UnicodeDown('A'), events[2]);
        Assert.Contains(events, e => e.VirtualKey == 0x31);
    }

    [Fact]
    public void Layout_mapper_is_ignored_in_unicode_mode()
    {
        var (events, _) = PlanAll("ab", new TypingOptions { Method = InputMethod.Unicode }, new FakeLayout());

        Assert.All(events, e => Assert.True(e.IsUnicode));
    }

    [Fact]
    public void Counts_untypeable_controls()
    {
        Assert.Equal(3, KeystrokePlanner.CountUntypeableControls("a\0b\bc\r\n\t\u007F"));
    }
}
