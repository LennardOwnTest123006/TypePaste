using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using TypePaste.ViewModels;

namespace TypePaste.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private long _textLength;
    private bool _allowClose;

    internal MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        viewModel.GetText = () => Editor.Text;
        viewModel.ReplaceText = ReplaceText;
        viewModel.InsertText = InsertText;
        viewModel.GetWindow = () => this;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        Editor.TextChanged += OnEditorTextChanged;
        Editor.PreviewDragOver += OnEditorDragOver;
        Editor.PreviewDrop += OnEditorDrop;

        SourceInitialized += (_, _) => App.Host?.Theme.ApplyWindowFrame(this);
        FitToWorkArea();
    }

    /// <summary>Raised when the window is closed for real (not hidden to the tray).</summary>
    public event EventHandler? ExitRequested;

    /// <summary>Raised the first time the window is hidden to the tray.</summary>
    public event EventHandler? HiddenToTray;

    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        if (App.Host?.Settings.CloseToTray == true)
        {
            Hide();
            HiddenToTray?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape && _viewModel.IsSettingsOpen && Keyboard.FocusedElement is not Controls.HotkeyBox { IsRecording: true })
        {
            _viewModel.IsSettingsOpen = false;
            e.Handled = true;
        }
    }

    private void ReplaceText(string text)
    {
        // Selecting and replacing keeps the change undoable (Ctrl+Z).
        Editor.SelectAll();
        Editor.SelectedText = text;
        Editor.CaretIndex = text.Length;
        Editor.ScrollToEnd();
    }

    private void InsertText(string text)
    {
        Editor.SelectedText = text;
        Editor.CaretIndex = Editor.SelectionStart + text.Length;
        Editor.SelectionLength = 0;
        Editor.Focus();
    }

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        foreach (var change in e.Changes)
        {
            _textLength += change.AddedLength - change.RemovedLength;
        }

        Placeholder.Visibility = _textLength <= 0 ? Visibility.Visible : Visibility.Collapsed;
        _viewModel.NotifyTextChanged();
    }

    private static void OnEditorDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnEditorDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            _viewModel.LoadFile(files[0]);
            e.Handled = true;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsSettingsOpen))
        {
            AnimateSettings(_viewModel.IsSettingsOpen);
        }
    }

    private void AnimateSettings(bool open)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(open ? 260 : 200);
        if (open)
        {
            SettingsLayer.Visibility = Visibility.Visible;
            MainContent.IsEnabled = false;
        }

        var slide = new DoubleAnimation(open ? 0 : SettingsSheet.ActualWidth + 40, duration) { EasingFunction = ease };
        var fade = new DoubleAnimation(open ? 1 : 0, duration);
        if (!open)
        {
            fade.Completed += (_, _) =>
            {
                if (!_viewModel.IsSettingsOpen)
                {
                    SettingsLayer.Visibility = Visibility.Collapsed;
                    MainContent.IsEnabled = true;
                    Editor.Focus();
                }
            };
        }

        SheetOffset.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slide);
        Scrim.BeginAnimation(OpacityProperty, fade);
    }

    private void Scrim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _viewModel.IsSettingsOpen = false;

    /// <summary>Keeps the default size within small screens (for example 1024×768 or high scaling).</summary>
    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, Math.Max(MinWidth, area.Width - 40));
        Height = Math.Min(Height, Math.Max(MinHeight, area.Height - 40));
    }
}
