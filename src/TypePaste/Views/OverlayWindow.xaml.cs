using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using static TypePaste.Native.NativeMethods;

namespace TypePaste.Views;

internal enum OverlayKind
{
    Countdown,
    Typing,
    Success,
    Warning,
    Error,
    Info,
}

/// <summary>
/// Small always-on-top status card shown at the top of the screen while TypePaste counts down or types. It never
/// takes focus and lets clicks pass through, so it cannot interfere with the target window.
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly DispatcherTimer _hideTimer;
    private bool _hiding;

    public OverlayWindow()
    {
        InitializeComponent();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _hideTimer.Tick += (_, _) => FadeOut();
    }

    internal void Present(OverlayKind kind, string title, string detail, double? progress, int? countdown, nint anchorWindow, TimeSpan? autoHide)
    {
        TitleText.Text = title;
        DetailText.Text = detail;
        Progress.Visibility = progress.HasValue ? Visibility.Visible : Visibility.Collapsed;
        Progress.Value = progress ?? 0;

        Logo.Visibility = kind is OverlayKind.Typing or OverlayKind.Info ? Visibility.Visible : Visibility.Collapsed;
        CountdownBadge.Visibility = kind == OverlayKind.Countdown ? Visibility.Visible : Visibility.Collapsed;
        Badge.Visibility = kind is OverlayKind.Success or OverlayKind.Warning or OverlayKind.Error ? Visibility.Visible : Visibility.Collapsed;

        if (kind == OverlayKind.Countdown && countdown.HasValue)
        {
            CountdownText.Text = countdown.Value.ToString(System.Globalization.CultureInfo.CurrentCulture);
            var pop = new DoubleAnimation(1.25, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            CountdownScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, pop);
            CountdownScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, pop);
        }

        (Badge.Background, BadgeGlyph.Foreground, BadgeGlyph.Text) = kind switch
        {
            OverlayKind.Success => (FindBrush("SuccessSoftBrush"), FindBrush("SuccessBrush"), "\uE73E"),
            OverlayKind.Warning => (FindBrush("WarningSoftBrush"), FindBrush("WarningBrush"), "\uE71A"),
            OverlayKind.Error => (FindBrush("DangerSoftBrush"), FindBrush("DangerBrush"), "\uE783"),
            _ => (Badge.Background, BadgeGlyph.Foreground, BadgeGlyph.Text),
        };

        _hideTimer.Stop();
        if (!IsVisible || _hiding)
        {
            _hiding = false;
            BeginAnimation(OpacityProperty, null);
            Opacity = 0;
            Show();
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
            Slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new DoubleAnimation(-12, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        UpdateLayout();
        PositionOnMonitorOf(anchorWindow);

        if (autoHide.HasValue)
        {
            _hideTimer.Interval = autoHide.Value;
            _hideTimer.Start();
        }
    }

    internal void UpdateProgress(double fraction, string detail)
    {
        Progress.Value = fraction;
        DetailText.Text = detail;
    }

    internal void FadeOut()
    {
        _hideTimer.Stop();
        if (!IsVisible || _hiding)
        {
            return;
        }

        _hiding = true;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            if (_hiding)
            {
                Hide();
                _hiding = false;
            }
        };
        BeginAnimation(OpacityProperty, fade);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, GWL_EXSTYLE);
        SetWindowLongPtr(handle, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED);
    }

    private System.Windows.Media.Brush? FindBrush(string key) => TryFindResource(key) as System.Windows.Media.Brush;

    /// <summary>Centers the card at the top of the work area of the monitor that shows the target window.</summary>
    private void PositionOnMonitorOf(nint anchorWindow)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0)
        {
            return;
        }

        nint monitor;
        if (anchorWindow != 0 && IsWindow(anchorWindow))
        {
            monitor = MonitorFromWindow(anchorWindow, MONITOR_DEFAULTTONEAREST);
        }
        else
        {
            GetCursorPos(out var cursor);
            monitor = MonitorFromPoint(cursor, MONITOR_DEFAULTTOPRIMARY);
        }

        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfoW(monitor, ref info) || !GetWindowRect(handle, out var rect))
        {
            return;
        }

        var x = info.rcWork.Left + ((info.rcWork.Width - rect.Width) / 2);
        var y = info.rcWork.Top + 12;
        SetWindowPos(handle, 0, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }
}
