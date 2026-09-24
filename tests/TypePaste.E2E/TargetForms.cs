using System.Drawing;
using System.Windows.Forms;

namespace TypePaste.E2E;

/// <summary>A window with one multi-line text field that TypePaste types into.</summary>
internal sealed class TargetForm : Form
{
    public TargetForm(string title, bool rich, Rectangle bounds)
    {
        Text = title;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        TopMost = true;
        ShowInTaskbar = true;
        Font = new Font("Segoe UI", 10f);

        Box = rich
            ? new RichTextBox { DetectUrls = false, WordWrap = true, ScrollBars = RichTextBoxScrollBars.Vertical, MaxLength = int.MaxValue }
            : new TextBox { Multiline = true, AcceptsReturn = true, AcceptsTab = true, WordWrap = true, ScrollBars = ScrollBars.Vertical, MaxLength = 0 };
        Box.Dock = DockStyle.Fill;
        if (Box is RichTextBox richBox)
        {
            richBox.AcceptsTab = true;
            richBox.LanguageOption = RichTextBoxLanguageOptions.UIFonts;
        }

        Controls.Add(Box);
    }

    public TextBoxBase Box { get; }
}

/// <summary>Hosts the WinForms target windows on their own UI thread (like a separate app would).</summary>
internal sealed class TargetHost : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private ApplicationContext? _context;

    public TargetHost()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Target UI" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException("Target windows did not start.");
        }
    }

    public TargetForm Plain { get; private set; } = null!;

    public TargetForm Rich { get; private set; } = null!;

    public TargetForm Other { get; private set; } = null!;

    public T Invoke<T>(Func<T> action) => Plain.Invoke(action);

    public void Invoke(Action action) => Plain.Invoke(action);

    /// <summary>Creates an extra form (used for the "target closed" test).</summary>
    public TargetForm CreateForm(string title, Rectangle bounds) => Invoke(() =>
    {
        var form = new TargetForm(title, rich: false, bounds);
        form.Show();
        return form;
    });

    public string GetText(TargetForm form) => Invoke(() => form.IsDisposed ? string.Empty : form.Box.Text);

    public int GetLength(TargetForm form) => Invoke(() => form.IsDisposed ? 0 : form.Box.TextLength);

    public void Clear(TargetForm form) => Invoke(() => form.Box.Clear());

    public nint Handle(TargetForm form) => Invoke(() => form.Handle);

    public nint BoxHandle(TargetForm form) => Invoke(() => form.Box.Handle);

    public bool BoxFocused(TargetForm form) => Invoke(() => form.Box.Focused);

    /// <summary>Shows only the given forms (others are hidden so they cannot cover other apps).</summary>
    public void ShowOnly(params TargetForm[] forms) => Invoke(() =>
    {
        foreach (var form in new[] { Plain, Rich, Other })
        {
            form.Visible = forms.Contains(form);
        }
    });

    /// <summary>Clicks into the text field like a user and verifies that it has keyboard focus.</summary>
    public void Focus(TargetForm form)
    {
        Invoke(() =>
        {
            form.Visible = true;
            form.BringToFront();
        });

        for (var attempt = 0; attempt < 4; attempt++)
        {
            Win32.ClickCenter(BoxHandle(form));
            Thread.Sleep(150);
            if (Win32.GetForegroundWindow() == Handle(form) && BoxFocused(form))
            {
                Invoke(() => form.Box.Select(form.Box.TextLength, 0));
                return;
            }
        }

        throw new InvalidOperationException($"Could not focus '{form.Text}'. Foreground: {Win32.Describe(Win32.GetForegroundWindow())}");
    }

    public void Dispose()
    {
        try
        {
            Invoke(() => _context?.ExitThread());
        }
        catch (Exception)
        {
            // Already gone.
        }
    }

    private void Run()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Plain = new TargetForm("E2E target — TextBox", rich: false, new Rectangle(24, 120, 460, 420));
        Rich = new TargetForm("E2E target — RichTextBox", rich: true, new Rectangle(500, 120, 460, 420));
        Other = new TargetForm("E2E other window", rich: false, new Rectangle(300, 560, 420, 160));
        Plain.Show();
        Rich.Show();
        Other.Show();
        _context = new ApplicationContext();
        _ready.Set();
        Application.Run(_context);
    }
}
