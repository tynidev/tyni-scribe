using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tts.App.Services;
using Tts.App.Services.Tray;
using Tts.App.ViewModels;

namespace Tts.App;

public partial class SettingsWindow : Window
{
    private readonly SettingsWindowViewModel _viewModel;
    private readonly TrayLifecycleService _trayLifecycle;
    private HotkeyCaptureTarget _hotkeyCaptureTarget = HotkeyCaptureTarget.None;

    public SettingsWindow(SettingsWindowViewModel viewModel, TrayLifecycleService trayLifecycle)
    {
        InitializeComponent();
        DataContext = viewModel;

        _viewModel = viewModel;
        _trayLifecycle = trayLifecycle;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.LoadCommand.IsRunning)
        {
            await _viewModel.LoadCommand.ExecuteAsync(null);
        }
    }

    private void OnStartStopHotkeyCaptureClicked(object sender, RoutedEventArgs e)
    {
        BeginHotkeyCapture(HotkeyCaptureTarget.StartStop);
    }

    private void OnCancelHotkeyCaptureClicked(object sender, RoutedEventArgs e)
    {
        BeginHotkeyCapture(HotkeyCaptureTarget.Cancel);
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (_hotkeyCaptureTarget == HotkeyCaptureTarget.None)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;
        var key = NormalizeKey(e);

        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            EndHotkeyCapture();
            return;
        }

        if (IsModifierKey(key))
        {
            return;
        }

        var gesture = new HotkeyGesture(Keyboard.Modifiers, key).ToString();
        if (!HotkeyGestureParser.TryParse(gesture, out _, out var errorMessage))
        {
            _viewModel.StatusMessage = errorMessage;
            return;
        }

        if (_hotkeyCaptureTarget == HotkeyCaptureTarget.StartStop)
        {
            _viewModel.StartStopHotkey = gesture;
        }
        else if (_hotkeyCaptureTarget == HotkeyCaptureTarget.Cancel)
        {
            _viewModel.CancelHotkey = gesture;
        }

        _viewModel.StatusMessage = "Hotkey captured. Save settings to apply it.";
        EndHotkeyCapture();
    }

    private void BeginHotkeyCapture(HotkeyCaptureTarget target)
    {
        _hotkeyCaptureTarget = target;
        _viewModel.StatusMessage = "Press the new hotkey, or Escape to cancel.";

        if (target == HotkeyCaptureTarget.StartStop)
        {
            StartStopHotkeyButton.SetCurrentValue(ContentControl.ContentProperty, "Press keys...");
            StartStopHotkeyButton.Focus();
            return;
        }

        CancelHotkeyButton.SetCurrentValue(ContentControl.ContentProperty, "Press keys...");
        CancelHotkeyButton.Focus();
    }

    private void EndHotkeyCapture()
    {
        _hotkeyCaptureTarget = HotkeyCaptureTarget.None;
        StartStopHotkeyButton.GetBindingExpression(ContentControl.ContentProperty)?.UpdateTarget();
        CancelHotkeyButton.GetBindingExpression(ContentControl.ContentProperty)?.UpdateTarget();
    }

    private static Key NormalizeKey(System.Windows.Input.KeyEventArgs eventArgs)
    {
        return eventArgs.Key switch
        {
            Key.System => eventArgs.SystemKey,
            Key.ImeProcessed => eventArgs.ImeProcessedKey,
            Key.DeadCharProcessed => eventArgs.DeadCharProcessedKey,
            _ => eventArgs.Key
        };
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl
            or Key.RightCtrl
            or Key.LeftAlt
            or Key.RightAlt
            or Key.LeftShift
            or Key.RightShift
            or Key.LWin
            or Key.RWin;
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_trayLifecycle.IsQuitRequested)
        {
            e.Cancel = true;
            _trayLifecycle.Quit();
            return;
        }

        base.OnClosing(e);
    }

    private enum HotkeyCaptureTarget
    {
        None,
        StartStop,
        Cancel
    }
}