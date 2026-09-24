namespace TypePaste.Views;

/// <summary>Creates the overlay window on demand and forwards status updates to it.</summary>
internal sealed class OverlayPresenter : IDisposable
{
    private OverlayWindow? _window;

    public bool Enabled { get; set; } = true;

    private OverlayWindow Window => _window ??= new OverlayWindow();

    public void ShowCountdown(int seconds, string detail, nint anchor)
    {
        if (Enabled)
        {
            Window.Present(OverlayKind.Countdown, $"Starting in {seconds}…", detail, null, seconds, anchor, null);
        }
    }

    public void ShowTyping(string title, string detail, nint anchor)
    {
        if (Enabled)
        {
            Window.Present(OverlayKind.Typing, title, detail, 0, null, anchor, null);
        }
    }

    public void UpdateProgress(double fraction, string detail)
    {
        if (Enabled && _window is { IsVisible: true })
        {
            _window.UpdateProgress(fraction, detail);
        }
    }

    /// <summary>Shows a result or message; it hides itself after <paramref name="duration"/>.</summary>
    public void ShowMessage(OverlayKind kind, string title, string detail, nint anchor, TimeSpan duration, bool force = false)
    {
        if (Enabled || force)
        {
            Window.Present(kind, title, detail, null, null, anchor, duration);
        }
    }

    public void Hide() => _window?.FadeOut();

    public void Dispose()
    {
        _window?.Close();
        _window = null;
    }
}
