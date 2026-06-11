using System.ComponentModel;
using System.Windows;
using Tts.App.Services.Tray;
using Tts.App.ViewModels;

namespace Tts.App;

public partial class SettingsWindow : Window
{
    private readonly SettingsWindowViewModel _viewModel;
    private readonly TrayLifecycleService _trayLifecycle;

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

    private void OnHideToTrayClicked(object sender, RoutedEventArgs e)
    {
        _trayLifecycle.HideSettingsWindow();
    }

    private void OnQuitClicked(object sender, RoutedEventArgs e)
    {
        _trayLifecycle.Quit();
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
            Hide();
        }

        base.OnClosing(e);
    }
}