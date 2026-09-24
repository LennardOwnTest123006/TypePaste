using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace TypePaste.E2E;

/// <summary>A WPF window with a TextBox on its own UI thread, like a third-party WPF app.</summary>
internal sealed class WpfTargetHost : IDisposable
{
    private readonly ManualResetEventSlim _ready = new();
    private Dispatcher _dispatcher = null!;
    private Window _window = null!;
    private System.Windows.Controls.TextBox _box = null!;

    public WpfTargetHost()
    {
        var thread = new Thread(() =>
        {
            _box = new System.Windows.Controls.TextBox
            {
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontSize = 14,
            };
            _window = new Window
            {
                Title = "E2E target — WPF TextBox",
                Left = 60,
                Top = 110,
                Width = 520,
                Height = 440,
                Topmost = true,
                Content = _box,
                WindowStartupLocation = WindowStartupLocation.Manual,
            };
            _window.Show();
            _dispatcher = Dispatcher.CurrentDispatcher;
            _ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WPF target UI",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException("WPF target window did not start.");
        }
    }

    public nint Handle => _dispatcher.Invoke(() => new WindowInteropHelper(_window).Handle);

    public string Text => _dispatcher.Invoke(() => _box.Text);

    public int Length => _dispatcher.Invoke(() => _box.Text.Length);

    public void Show(bool visible) => _dispatcher.Invoke(() =>
    {
        if (visible)
        {
            _window.Show();
        }
        else
        {
            _window.Hide();
        }
    });

    public void Clear() => _dispatcher.Invoke(() => _box.Clear());

    public void Focus()
    {
        Show(true);
        var handle = Handle;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            Win32.ClickCenter(handle);
            Thread.Sleep(200);
            if (Win32.GetForegroundWindow() == handle && _dispatcher.Invoke(() => _box.IsKeyboardFocused))
            {
                _dispatcher.Invoke(() => _box.CaretIndex = _box.Text.Length);
                return;
            }
        }

        throw new CheckFailedException($"WPF target did not get focus (foreground: {Win32.Describe(Win32.GetForegroundWindow())})");
    }

    public void Dispose()
    {
        try
        {
            _dispatcher.InvokeShutdown();
        }
        catch (Exception)
        {
            // Already gone.
        }
    }
}

internal sealed class WpfTypingTarget(WpfTargetHost host) : ITypingTarget
{
    public string Name => "WPF TextBox";

    public int Length => host.Length;

    public void Prepare()
    {
        host.Clear();
        host.Focus();
    }

    // A WPF TextBox with AcceptsReturn inserts "\r\n" for Enter, like an edit control.
    public TimeSpan WaitForText(string source, TimeSpan timeout) =>
        TargetWait.ForText(() => Samples.AsEditControl(host.Text), Samples.AsEditControl(source), timeout);

    public int SettledLength() => Wait.Stable(() => Length, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
}
