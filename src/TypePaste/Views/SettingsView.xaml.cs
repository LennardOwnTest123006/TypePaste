using System.Windows.Controls;
using TypePaste.Controls;
using TypePaste.Core.Hotkeys;
using TypePaste.ViewModels;

namespace TypePaste.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        Wire(StartHotkeyBox, (vm, gesture) => vm.RecordStartHotkey(gesture));
        Wire(StopHotkeyBox, (vm, gesture) => vm.RecordStopHotkey(gesture));
    }

    private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    private void Wire(HotkeyBox box, Action<SettingsViewModel, HotkeyGesture> record)
    {
        box.RecordingStarted += (_, _) => ViewModel?.BeginHotkeyRecording();
        box.RecordingEnded += (_, _) => ViewModel?.EndHotkeyRecording();
        box.GestureRecorded += (_, gesture) =>
        {
            if (ViewModel is { } vm)
            {
                record(vm, gesture);
            }
        };
    }
}
