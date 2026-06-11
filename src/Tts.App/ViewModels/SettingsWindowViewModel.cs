using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tts.App.Configuration;

namespace Tts.App.ViewModels;

public sealed partial class SettingsWindowViewModel : ObservableObject
{
    private readonly IAppSettingsStore _settingsStore;
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

    public SettingsWindowViewModel(IAppSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;

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
        _settings.ConfigVersion = AppSettings.CurrentConfigVersion;
        _settings.SelectedMicrophoneDeviceId = string.IsNullOrWhiteSpace(SelectedMicrophoneDeviceId)
            ? null
            : SelectedMicrophoneDeviceId.Trim();
        _settings.StartStopHotkey.Gesture = StartStopHotkey.Trim();
        _settings.CancelHotkey.Gesture = CancelHotkey.Trim();
        _settings.SelectedTranscriptionProviderId = SelectedTranscriptionProviderId.Trim();
        _settings.Cleanup.IsEnabled = IsCleanupEnabled;
        _settings.Cleanup.Prompt = CleanupPrompt.Trim();
        _settings.EnabledOutputProviderIds = EnabledOutputProviders
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .DefaultIfEmpty("clipboard")
            .ToList();

        await _settingsStore.SaveAsync(_settings);
        StatusMessage = "Settings saved";
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