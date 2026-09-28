using System.Runtime.InteropServices;
using System.Windows;
using static TypePaste.Native.NativeMethods;

namespace TypePaste.Services;

/// <summary>A notification area (system tray) icon with a native context menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint IconId = 1;
    private const int CallbackMessage = WM_APP + 1;

    private readonly MessageWindow _window;
    private nint _icon;
    private string _tooltip;
    private bool _added;

    public TrayIcon(MessageWindow window, string tooltip)
    {
        _window = window;
        _tooltip = tooltip;
        _icon = IconLoader.LoadAppIcon(GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForSystem()));
        _window.MessageReceived += OnMessage;
        _window.TaskbarCreated += OnTaskbarCreated;
    }

    /// <summary>Left click (open the main window).</summary>
    public event EventHandler? Activated;

    /// <summary>Builds the context menu items right before the menu is shown.</summary>
    public Func<IReadOnlyList<TrayMenuItem>>? MenuProvider { get; set; }

    public void Show()
    {
        var data = CreateData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        _added = Shell_NotifyIconW(NIM_ADD, ref data);
        if (!_added)
        {
            App.Log.Warn("Could not add the notification area icon.");
        }
    }

    public void SetTooltip(string tooltip)
    {
        _tooltip = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        if (_added)
        {
            var data = CreateData(NIF_TIP);
            Shell_NotifyIconW(NIM_MODIFY, ref data);
        }
    }

    /// <summary>Shows a notification balloon (a toast on Windows 10/11).</summary>
    public void ShowNotification(string title, string text)
    {
        if (!_added)
        {
            return;
        }

        var data = CreateData(NIF_INFO);
        data.szInfoTitle = title.Length > 63 ? title[..63] : title;
        data.szInfo = text.Length > 255 ? text[..255] : text;
        data.dwInfoFlags = NIIF_USER | NIIF_LARGE_ICON;
        data.hBalloonIcon = IconLoader.LoadAppIcon(GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForSystem()) * 2);
        Shell_NotifyIconW(NIM_MODIFY, ref data);
        if (data.hBalloonIcon != 0)
        {
            DestroyIcon(data.hBalloonIcon);
        }
    }

    public void Dispose()
    {
        _window.MessageReceived -= OnMessage;
        _window.TaskbarCreated -= OnTaskbarCreated;
        if (_added)
        {
            var data = CreateData(0);
            Shell_NotifyIconW(NIM_DELETE, ref data);
            _added = false;
        }

        if (_icon != 0)
        {
            DestroyIcon(_icon);
            _icon = 0;
        }
    }

    private NOTIFYICONDATAW CreateData(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _window.Handle,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _tooltip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private void OnTaskbarCreated(object? sender, EventArgs e)
    {
        _added = false;
        Show();
    }

    private bool OnMessage(int msg, nint wParam, nint lParam)
    {
        if (msg != CallbackMessage)
        {
            return false;
        }

        switch ((int)lParam & 0xFFFF)
        {
            case WM_LBUTTONUP:
            case WM_LBUTTONDBLCLK:
                Activated?.Invoke(this, EventArgs.Empty);
                break;
            case WM_RBUTTONUP:
            case WM_CONTEXTMENU:
                ShowMenu();
                break;
        }

        return true;
    }

    private void ShowMenu()
    {
        var items = MenuProvider?.Invoke();
        if (items is null || items.Count == 0)
        {
            return;
        }

        var menu = CreatePopupMenu();
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.IsSeparator)
                {
                    AppendMenuW(menu, MF_SEPARATOR, 0, null);
                    continue;
                }

                var flags = MF_STRING | (item.IsChecked ? MF_CHECKED : 0) | (item.IsEnabled ? 0 : MF_GRAYED);
                AppendMenuW(menu, flags, (nuint)(i + 1), item.Text);
                if (item.IsDefault)
                {
                    SetMenuDefaultItem(menu, (uint)(i + 1), 0);
                }
            }

            GetCursorPos(out var point);

            // Required so the menu closes when the user clicks elsewhere.
            SetForegroundWindow(_window.Handle);
            var command = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY | TPM_BOTTOMALIGN | TPM_RIGHTALIGN, point.X, point.Y, _window.Handle, 0);
            PostMessageW(_window.Handle, WM_NULL, 0, 0);

            if (command > 0 && command <= items.Count)
            {
                var action = items[command - 1].Action;
                Application.Current?.Dispatcher.BeginInvoke(() => action?.Invoke());
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }
}

internal sealed record TrayMenuItem(string Text, Action? Action = null, bool IsChecked = false, bool IsDefault = false, bool IsEnabled = true)
{
    public bool IsSeparator => Text.Length == 0;

    public static TrayMenuItem Separator { get; } = new(string.Empty);
}
