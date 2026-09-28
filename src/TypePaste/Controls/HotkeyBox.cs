using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TypePaste.Core.Hotkeys;

namespace TypePaste.Controls;

/// <summary>
/// Displays a hotkey as keycaps; click it and press a key combination to record a new one.
/// </summary>
public sealed class HotkeyBox : Control
{
    public static readonly DependencyProperty GestureProperty = DependencyProperty.Register(
        nameof(Gesture), typeof(HotkeyGesture), typeof(HotkeyBox), new FrameworkPropertyMetadata(default(HotkeyGesture)));

    public static readonly DependencyProperty IsRecordingProperty = DependencyProperty.Register(
        nameof(IsRecording), typeof(bool), typeof(HotkeyBox), new PropertyMetadata(false));

    public HotkeyGesture Gesture
    {
        get => (HotkeyGesture)GetValue(GestureProperty);
        set => SetValue(GestureProperty, value);
    }

    public bool IsRecording
    {
        get => (bool)GetValue(IsRecordingProperty);
        private set => SetValue(IsRecordingProperty, value);
    }

    /// <summary>Recording started (global hotkeys should be suspended so they do not fire).</summary>
    public event EventHandler? RecordingStarted;

    /// <summary>Recording ended (captured or cancelled).</summary>
    public event EventHandler? RecordingEnded;

    /// <summary>A new combination was pressed. The handler validates and applies it.</summary>
    public event EventHandler<HotkeyGesture>? GestureRecorded;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        StartRecording();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsRecording && e.Key is Key.Enter or Key.Space)
        {
            StartRecording();
            e.Handled = true;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (!IsRecording)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
        {
            return;
        }

        var modifiers = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows))
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        var gesture = new HotkeyGesture(modifiers, (ushort)KeyInterop.VirtualKeyFromKey(key));
        StopRecording();
        GestureRecorded?.Invoke(this, gesture);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        StopRecording();
    }

    private void StartRecording()
    {
        if (IsRecording)
        {
            return;
        }

        IsRecording = true;
        RecordingStarted?.Invoke(this, EventArgs.Empty);
    }

    private void StopRecording()
    {
        if (!IsRecording)
        {
            return;
        }

        IsRecording = false;
        RecordingEnded?.Invoke(this, EventArgs.Empty);
    }
}
