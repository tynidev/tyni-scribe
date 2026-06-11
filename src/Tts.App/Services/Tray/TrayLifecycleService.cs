using System.Drawing;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Forms = System.Windows.Forms;

namespace Tts.App.Services.Tray;

public sealed class TrayLifecycleService : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private Icon? _icon;
    private bool _ownsIcon;
    private Forms.NotifyIcon? _notifyIcon;
    private SettingsWindow? _settingsWindow;

    public TrayLifecycleService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public bool IsQuitRequested { get; private set; }

    public void Initialize()
    {
        if (_notifyIcon is not null)
        {
            return;
        }

        var openSettingsItem = new Forms.ToolStripMenuItem("Open Settings", null, (_, _) => ShowSettingsWindow());
        var quitItem = new Forms.ToolStripMenuItem("Quit", null, (_, _) => Quit());

        var contextMenu = new Forms.ContextMenuStrip();
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