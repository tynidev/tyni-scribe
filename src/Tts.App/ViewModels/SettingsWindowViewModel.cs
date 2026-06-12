using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tts.App.Configuration;
using Tts.App.Services;
using Tts.App.Services.Audio;
using Tts.App.Services.Transcription;

namespace Tts.App.ViewModels;

public sealed partial class SettingsWindowViewModel : ObservableObject
{
    private readonly IAppSettingsStore _settingsStore;
    private readonly ISessionOrchestrator _sessionOrchestrator;
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly IAudioCaptureService _audioCaptureService;
    private readonly IReadOnlyList<TranscriptionProviderOption> _availableTranscriptionProviders;
    private AppSettings _settings = new();
    private bool _isRefreshingMicrophones;

    [ObservableProperty]
    private int _configVersion;

    [ObservableProperty]
    private string _selectedMicrophoneDeviceId = string.Empty;

    [ObservableProperty]
    private double _microphoneLevel;

    [ObservableProperty]
    private string _microphoneLevelText = "0%";

    [ObservableProperty]
    private string _microphoneStatus = "Microphones are not loaded.";

    [ObservableProperty]
    private bool _isLevelMonitoring;

    [ObservableProperty]
    private string _levelMonitoringButtonText = "Start Meter";

    [ObservableProperty]
    private string _startStopHotkey = string.Empty;

    [ObservableProperty]
    private string _cancelHotkey = string.Empty;

    [ObservableProperty]
    private string _selectedTranscriptionProviderId = string.Empty;

    [ObservableProperty]
    private bool _isWhisperCppProviderSelected = true;

    [ObservableProperty]
    private string _selectedAudioProcessorProviderId = string.Empty;

    [ObservableProperty]
    private string _selectedWhisperCppModelId = string.Empty;

    [ObservableProperty]
    private string _transcriptionLanguage = string.Empty;

    [ObservableProperty]
    private int _transcriptionTimeoutSeconds;

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
        IGlobalHotkeyService hotkeyService,
        IAudioCaptureService audioCaptureService,
        IEnumerable<IBatchTranscriptionProvider> batchTranscriptionProviders)
    {
        _settingsStore = settingsStore;
        _sessionOrchestrator = sessionOrchestrator;
        _hotkeyService = hotkeyService;
        _audioCaptureService = audioCaptureService;
        _availableTranscriptionProviders = batchTranscriptionProviders
            .Select(provider => new TranscriptionProviderOption(provider.Metadata.Id, provider.Metadata.DisplayName))
            .OrderBy(provider => provider.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        SessionState = _sessionOrchestrator.State.ToString();
        SessionStatus = _sessionOrchestrator.StatusMessage;
        HotkeyStatus = _hotkeyService.StatusMessage;

        _sessionOrchestrator.StateChanged += OnSessionStateChanged;
        _hotkeyService.RegistrationStatusChanged += OnHotkeyRegistrationStatusChanged;
        _audioCaptureService.LevelChanged += OnMicrophoneLevelChanged;

        LoadCommand = new AsyncRelayCommand(LoadAsync);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        RefreshMicrophonesCommand = new AsyncRelayCommand(RefreshMicrophonesAsync);
        ToggleLevelMonitoringCommand = new AsyncRelayCommand(ToggleLevelMonitoringAsync);
        OpenSettingsFolderCommand = new RelayCommand(OpenSettingsFolder);
    }

    public ObservableCollection<MicrophoneDeviceOption> MicrophoneDevices { get; } = new();

    public ObservableCollection<TranscriptionProviderOption> TranscriptionProviders { get; } = new();

    public ObservableCollection<TranscriptionModelOption> TranscriptionModels { get; } = new()
    {
        new TranscriptionModelOption(WhisperCppModelCatalog.TinyEnglishModelId, "Tiny English (fastest)"),
        new TranscriptionModelOption(WhisperCppModelCatalog.BaseEnglishModelId, "Base English (balanced)"),
        new TranscriptionModelOption(WhisperCppModelCatalog.SmallEnglishModelId, "Small English (better accuracy)"),
        new TranscriptionModelOption(WhisperCppModelCatalog.LargeV3TurboModelId, "Large v3 Turbo (best local quality)")
    };

    public string ConfigPath => _settingsStore.SettingsFilePath;

    public IAsyncRelayCommand LoadCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public IAsyncRelayCommand RefreshMicrophonesCommand { get; }

    public IAsyncRelayCommand ToggleLevelMonitoringCommand { get; }

    public IRelayCommand OpenSettingsFolderCommand { get; }

    private async Task LoadAsync()
    {
        _settings = await _settingsStore.LoadAsync();
        var selectedMicrophoneDeviceId = _settings.SelectedMicrophoneDeviceId ?? string.Empty;

        ConfigVersion = _settings.ConfigVersion;
        LoadTranscriptionProviders();
        await RefreshMicrophonesAsync(selectedMicrophoneDeviceId);
        SelectedMicrophoneDeviceId = MicrophoneDevices.Any(device => device.Id == selectedMicrophoneDeviceId)
            ? selectedMicrophoneDeviceId
            : string.Empty;
        StartStopHotkey = _settings.StartStopHotkey.Gesture;
        CancelHotkey = _settings.CancelHotkey.Gesture;
        SelectedTranscriptionProviderId = ResolveSelectedTranscriptionProviderId(_settings.SelectedTranscriptionProviderId);
        SelectedAudioProcessorProviderId = _settings.SelectedAudioProcessorProviderId;
        SelectedWhisperCppModelId = TranscriptionModels.Any(model => model.Id == _settings.Transcription.WhisperCppModelId)
            ? _settings.Transcription.WhisperCppModelId
            : WhisperCppModelCatalog.TinyEnglishModelId;
        TranscriptionLanguage = _settings.Transcription.Language;
        TranscriptionTimeoutSeconds = _settings.Transcription.TimeoutSeconds;
        IsCleanupEnabled = _settings.Cleanup.IsEnabled;
        CleanupPrompt = _settings.Cleanup.Prompt;
        EnabledOutputProviders = string.Join(", ", _settings.EnabledOutputProviderIds);
        StatusMessage = "Settings loaded";
    }

    private async Task RefreshMicrophonesAsync()
    {
        var selectedMicrophoneDeviceId = SelectedMicrophoneDeviceId;
        await RefreshMicrophonesAsync(selectedMicrophoneDeviceId);
    }

    private async Task RefreshMicrophonesAsync(string preferredMicrophoneDeviceId)
    {
        preferredMicrophoneDeviceId = preferredMicrophoneDeviceId.Trim();
        var previousMicrophoneDeviceId = SelectedMicrophoneDeviceId;
        var nextMicrophoneDeviceId = string.Empty;

        try
        {
            var devices = await _audioCaptureService.EnumerateMicrophonesAsync();

            _isRefreshingMicrophones = true;

            try
            {
                MicrophoneDevices.Clear();
                MicrophoneDevices.Add(new MicrophoneDeviceOption(string.Empty, "System default microphone"));

                foreach (var device in devices)
                {
                    var displayName = device.IsDefault ? $"{device.DisplayName} (Default)" : device.DisplayName;
                    MicrophoneDevices.Add(new MicrophoneDeviceOption(device.Id, displayName));
                }

                nextMicrophoneDeviceId = MicrophoneDevices.Any(device => device.Id == preferredMicrophoneDeviceId)
                    ? preferredMicrophoneDeviceId
                    : string.Empty;
                SelectedMicrophoneDeviceId = nextMicrophoneDeviceId;
            }
            finally
            {
                _isRefreshingMicrophones = false;
            }

            MicrophoneStatus = devices.Count == 0
                ? "No active microphone devices found."
                : $"{devices.Count} active microphone device(s) found.";
        }
        catch (Exception exception)
        {
            _isRefreshingMicrophones = true;

            try
            {
                MicrophoneDevices.Clear();
                MicrophoneDevices.Add(new MicrophoneDeviceOption(string.Empty, "System default microphone"));
                SelectedMicrophoneDeviceId = string.Empty;
            }
            finally
            {
                _isRefreshingMicrophones = false;
            }

            MicrophoneStatus = $"Could not load microphones: {exception.Message}";
        }

        if (IsLevelMonitoring && previousMicrophoneDeviceId != nextMicrophoneDeviceId)
        {
            await StartLevelMonitoringAsync();
        }
    }

    private async Task ToggleLevelMonitoringAsync()
    {
        if (IsLevelMonitoring)
        {
            await StopLevelMonitoringAsync();
            return;
        }

        await StartLevelMonitoringAsync();
    }

    private async Task StartLevelMonitoringAsync()
    {
        try
        {
            await _audioCaptureService.StartLevelMonitoringAsync(NormalizeMicrophoneDeviceId(SelectedMicrophoneDeviceId));
            IsLevelMonitoring = true;
            LevelMonitoringButtonText = "Stop Meter";
            MicrophoneStatus = "Level meter active.";
        }
        catch (Exception exception)
        {
            IsLevelMonitoring = false;
            LevelMonitoringButtonText = "Start Meter";
            MicrophoneStatus = $"Could not start level meter: {exception.Message}";
        }
    }

    private async Task StopLevelMonitoringAsync()
    {
        try
        {
            await _audioCaptureService.StopLevelMonitoringAsync();
            MicrophoneStatus = "Level meter stopped.";
        }
        catch (Exception exception)
        {
            MicrophoneStatus = $"Could not stop level meter: {exception.Message}";
        }
        finally
        {
            IsLevelMonitoring = false;
            LevelMonitoringButtonText = "Start Meter";
            MicrophoneLevel = 0;
            MicrophoneLevelText = "0%";
        }
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
            SelectedTranscriptionProviderId = ResolveSelectedTranscriptionProviderId(SelectedTranscriptionProviderId),
            SelectedAudioProcessorProviderId = SelectedAudioProcessorProviderId.Trim(),
            Transcription = new TranscriptionSettings
            {
                WhisperCppModelId = SelectedWhisperCppModelId.Trim(),
                WhisperCppExecutablePathOverride = _settings.Transcription.WhisperCppExecutablePathOverride,
                WhisperModelPathOverride = _settings.Transcription.WhisperModelPathOverride,
                Language = TranscriptionLanguage.Trim(),
                TimeoutSeconds = TranscriptionTimeoutSeconds
            },
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

    private void LoadTranscriptionProviders()
    {
        TranscriptionProviders.Clear();

        foreach (var provider in _availableTranscriptionProviders)
        {
            TranscriptionProviders.Add(provider);
        }
    }

    private string ResolveSelectedTranscriptionProviderId(string providerId)
    {
        if (TranscriptionProviders.Any(provider => provider.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase)))
        {
            return providerId;
        }

        if (TranscriptionProviders.Any(provider => provider.Id.Equals(WhisperCppBatchTranscriptionProvider.ProviderId, StringComparison.OrdinalIgnoreCase)))
        {
            return WhisperCppBatchTranscriptionProvider.ProviderId;
        }

        return TranscriptionProviders.FirstOrDefault()?.Id ?? providerId.Trim();
    }

    partial void OnSelectedTranscriptionProviderIdChanged(string value)
    {
        IsWhisperCppProviderSelected = IsWhisperCppProvider(value);
    }

    private static bool IsWhisperCppProvider(string providerId)
    {
        return providerId.Equals(WhisperCppBatchTranscriptionProvider.ProviderId, StringComparison.OrdinalIgnoreCase)
            || providerId.Equals(WhisperWarmBatchTranscriptionProvider.ProviderId, StringComparison.OrdinalIgnoreCase)
            || providerId.Equals(WhisperNativeBatchTranscriptionProvider.ProviderId, StringComparison.OrdinalIgnoreCase);
    }

    private void OnSessionStateChanged(object? sender, SessionStateChangedEventArgs eventArgs)
    {
        RunOnUiThread(() =>
        {
            SessionState = eventArgs.CurrentState.ToString();
            SessionStatus = eventArgs.StatusMessage;

            if (eventArgs.CurrentState == AppSessionState.Recording)
            {
                IsLevelMonitoring = false;
                LevelMonitoringButtonText = "Start Meter";
                MicrophoneStatus = "Recording level active.";
            }
        });
    }

    private void OnHotkeyRegistrationStatusChanged(object? sender, HotkeyRegistrationStatusChangedEventArgs eventArgs)
    {
        RunOnUiThread(() => HotkeyStatus = eventArgs.Message);
    }

    private void OnMicrophoneLevelChanged(object? sender, MicrophoneLevelChangedEventArgs eventArgs)
    {
        RunOnUiThread(() =>
        {
            MicrophoneLevel = eventArgs.Level;
            MicrophoneLevelText = $"{eventArgs.Level:P0}";
        });
    }

    partial void OnSelectedMicrophoneDeviceIdChanged(string value)
    {
        if (IsLevelMonitoring && !_isRefreshingMicrophones)
        {
            _ = StartLevelMonitoringAsync();
        }
    }

    private static string? NormalizeMicrophoneDeviceId(string microphoneDeviceId)
    {
        return string.IsNullOrWhiteSpace(microphoneDeviceId) ? null : microphoneDeviceId.Trim();
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