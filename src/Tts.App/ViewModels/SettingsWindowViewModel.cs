using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tts.App.Configuration;
using Tts.App.Services;

namespace Tts.App.ViewModels;

public sealed partial class SettingsWindowViewModel : ObservableObject
{
    private readonly IAppSettingsStore _settingsStore;
    private readonly ISessionOrchestrator _sessionOrchestrator;
    private readonly IGlobalHotkeyService _hotkeyService;
    private AppSettings _settings = new();

    [ObservableProperty]
    private int _configVersion;

    [ObservableProperty]
    private string _selectedMicrophoneDeviceId = string.Empty;

    [ObservableProperty]
    private string _startStopHotkey = string.Empty;

    [ObservableProperty]
    private string _cancelHotkey = string.Empty;

    [ObservableProperty]
    private string _selectedTranscriptionProviderId = string.Empty;

    [ObservableProperty]
    private bool _isCleanupEnabled;

    [ObservableProperty]
    private string _cleanupPrompt = string.Empty;

    [ObservableProperty]
    private string _enabledOutputProviders = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _sessionState = AppSessionState.Idle.ToString();

    [ObservableProperty]
    private string _sessionStatus = "Ready";

    [ObservableProperty]
    private string _hotkeyStatus = "Hotkeys are not registered.";

    public SettingsWindowViewModel(
        IAppSettingsStore settingsStore,
        ISessionOrchestrator sessionOrchestrator,
        IGlobalHotkeyService hotkeyService)
    {
        _settingsStore = settingsStore;
        _sessionOrchestrator = sessionOrchestrator;
        _hotkeyService = hotkeyService;

        SessionState = _sessionOrchestrator.State.ToString();
        SessionStatus = _sessionOrchestrator.StatusMessage;
        HotkeyStatus = _hotkeyService.StatusMessage;

        _sessionOrchestrator.StateChanged += OnSessionStateChanged;
        _hotkeyService.RegistrationStatusChanged += OnHotkeyRegistrationStatusChanged;

        LoadCommand = new AsyncRelayCommand(LoadAsync);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        OpenSettingsFolderCommand = new RelayCommand(OpenSettingsFolder);
    }

    public string ConfigPath => _settingsStore.SettingsFilePath;

    public IAsyncRelayCommand LoadCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public IRelayCommand OpenSettingsFolderCommand { get; }

    private async Task LoadAsync()
    {
        _settings = await _settingsStore.LoadAsync();

        ConfigVersion = _settings.ConfigVersion;
        SelectedMicrophoneDeviceId = _settings.SelectedMicrophoneDeviceId ?? string.Empty;
        StartStopHotkey = _settings.StartStopHotkey.Gesture;
        CancelHotkey = _settings.CancelHotkey.Gesture;
        SelectedTranscriptionProviderId = _settings.SelectedTranscriptionProviderId;
        IsCleanupEnabled = _settings.Cleanup.IsEnabled;
        CleanupPrompt = _settings.Cleanup.Prompt;
        EnabledOutputProviders = string.Join(", ", _settings.EnabledOutputProviderIds);
        StatusMessage = "Settings loaded";
    }

    private async Task SaveAsync()
    {
        var nextSettings = new AppSettings
        {
            ConfigVersion = AppSettings.CurrentConfigVersion,
            SelectedMicrophoneDeviceId = string.IsNullOrWhiteSpace(SelectedMicrophoneDeviceId)
                ? null
                : SelectedMicrophoneDeviceId.Trim(),
            StartStopHotkey = HotkeySettings.FromGesture(StartStopHotkey.Trim()),
            CancelHotkey = HotkeySettings.FromGesture(CancelHotkey.Trim()),
            SelectedTranscriptionProviderId = SelectedTranscriptionProviderId.Trim(),
            Cleanup = new CleanupSettings
            {
                IsEnabled = IsCleanupEnabled,
                ProviderId = _settings.Cleanup.ProviderId,
                Prompt = CleanupPrompt.Trim()
            },
            EnabledOutputProviderIds = EnabledOutputProviders
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .DefaultIfEmpty("clipboard")
                .ToList(),
            SettingsWindow = _settings.SettingsWindow
        };

        var hotkeyResult = await _hotkeyService.ApplySettingsAsync(nextSettings);

        if (!hotkeyResult.Succeeded)
        {
            StatusMessage = hotkeyResult.Message;
            return;
        }

        _settings = nextSettings;
        await _settingsStore.SaveAsync(_settings);
        StatusMessage = "Settings saved";
    }

    private void OnSessionStateChanged(object? sender, SessionStateChangedEventArgs eventArgs)
    {
        RunOnUiThread(() =>
        {
            SessionState = eventArgs.CurrentState.ToString();
            SessionStatus = eventArgs.StatusMessage;
        });
    }

    private void OnHotkeyRegistrationStatusChanged(object? sender, HotkeyRegistrationStatusChangedEventArgs eventArgs)
    {
        RunOnUiThread(() => HotkeyStatus = eventArgs.Message);
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;

        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    private void OpenSettingsFolder()
    {
        var settingsDirectory = Path.GetDirectoryName(ConfigPath);

        if (string.IsNullOrWhiteSpace(settingsDirectory))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = settingsDirectory,
            UseShellExecute = true
        });
    }
}