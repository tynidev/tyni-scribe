using System.Drawing;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Forms = System.Windows.Forms;

namespace Tts.App.Services.Tray;

public sealed class TrayLifecycleService : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISessionOrchestrator _sessionOrchestrator;
    private Icon? _icon;
    private bool _ownsIcon;
    private Forms.ToolStripMenuItem? _statusItem;
    private Forms.ToolStripMenuItem? _startStopItem;
    private Forms.ToolStripMenuItem? _cancelItem;
    private Forms.ToolStripMenuItem? _retryOutputItem;
    private Forms.ToolStripMenuItem? _dismissOutputItem;
    private Forms.NotifyIcon? _notifyIcon;
    private SettingsWindow? _settingsWindow;

    public TrayLifecycleService(IServiceProvider serviceProvider, ISessionOrchestrator sessionOrchestrator)
    {
        _serviceProvider = serviceProvider;
        _sessionOrchestrator = sessionOrchestrator;
        _sessionOrchestrator.StateChanged += OnSessionStateChanged;
    }

    public bool IsQuitRequested { get; private set; }

    public void Initialize()
    {
        if (_notifyIcon is not null)
        {
            return;
        }

        _statusItem = new Forms.ToolStripMenuItem("Status: Idle")
        {
            Enabled = false
        };
        _startStopItem = new Forms.ToolStripMenuItem("Start Recording", null, async (_, _) => await _sessionOrchestrator.HandleStartStopAsync());
        _cancelItem = new Forms.ToolStripMenuItem("Cancel Session", null, async (_, _) => await _sessionOrchestrator.CancelAsync());
        _retryOutputItem = new Forms.ToolStripMenuItem("Retry Output", null, async (_, _) => await _sessionOrchestrator.RetryOutputAsync());
        _dismissOutputItem = new Forms.ToolStripMenuItem("Dismiss Output", null, async (_, _) => await _sessionOrchestrator.DismissPendingOutputAsync());
        var openSettingsItem = new Forms.ToolStripMenuItem("Open Settings", null, (_, _) => ShowSettingsWindow());
        var quitItem = new Forms.ToolStripMenuItem("Quit", null, (_, _) => Quit());

        var contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Items.Add(_statusItem);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(_startStopItem);
        contextMenu.Items.Add(_cancelItem);
        contextMenu.Items.Add(_retryOutputItem);
        contextMenu.Items.Add(_dismissOutputItem);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(openSettingsItem);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(quitItem);

        _icon = LoadIcon(out _ownsIcon);

        _notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = contextMenu,
            Icon = _icon,
            Text = "Speech-to-Text Daemon - Idle",
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => ShowSettingsWindow();
        UpdateTrayStatus(_sessionOrchestrator.State, _sessionOrchestrator.StatusMessage);
    }

    public void ShowSettingsWindow()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _settingsWindow ??= _serviceProvider.GetRequiredService<SettingsWindow>();
            System.Windows.Application.Current.MainWindow = _settingsWindow;

            if (!_settingsWindow.IsVisible)
            {
                _settingsWindow.Show();
            }

            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            _settingsWindow.Activate();
        });
    }

    public void HideSettingsWindow()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() => _settingsWindow?.Hide());
    }

    public void Quit()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            IsQuitRequested = true;
            Dispose();
            System.Windows.Application.Current.Shutdown();
        });
    }

    public void Dispose()
    {
        _sessionOrchestrator.StateChanged -= OnSessionStateChanged;

        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _notifyIcon = null;

        if (_ownsIcon)
        {
            _icon?.Dispose();
        }

        _icon = null;
        _ownsIcon = false;
    }

    private void OnSessionStateChanged(object? sender, SessionStateChangedEventArgs eventArgs)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() => UpdateTrayStatus(eventArgs.CurrentState, eventArgs.StatusMessage));
    }

    private void UpdateTrayStatus(AppSessionState state, string statusMessage)
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Text = TruncateTooltip($"Speech-to-Text Daemon - {state}");

        if (_statusItem is not null)
        {
            _statusItem.Text = $"Status: {state}";
        }

        if (_startStopItem is not null)
        {
            _startStopItem.Text = state == AppSessionState.Recording ? "Stop Recording" : "Start Recording";
            _startStopItem.Enabled = state is AppSessionState.Idle or AppSessionState.Recording;
        }

        if (_cancelItem is not null)
        {
            _cancelItem.Enabled = state is AppSessionState.Recording or AppSessionState.Processing;
        }

        var hasPendingOutput = _sessionOrchestrator.HasPendingOutput;

        if (_retryOutputItem is not null)
        {
            _retryOutputItem.Visible = hasPendingOutput;
            _retryOutputItem.Enabled = state == AppSessionState.Error && hasPendingOutput;
        }

        if (_dismissOutputItem is not null)
        {
            _dismissOutputItem.Visible = hasPendingOutput;
            _dismissOutputItem.Enabled = state == AppSessionState.Error && hasPendingOutput;
        }
    }

    private static string TruncateTooltip(string text)
    {
        const int maxLength = 63;
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static Icon LoadIcon(out bool ownsIcon)
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "speech-to-text-daemon.ico");

        if (File.Exists(iconPath))
        {
            ownsIcon = true;
            return new Icon(iconPath);
        }

        ownsIcon = false;
        return SystemIcons.Application;
    }
}