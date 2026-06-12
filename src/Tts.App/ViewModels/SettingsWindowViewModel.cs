using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tts.App.Configuration;
using Tts.App.Services;
using Tts.App.Services.Audio;
using Tts.App.Services.AudioProcessing;
using Tts.App.Services.Output;
using Tts.App.Services.Transcription;

namespace Tts.App.ViewModels;

public sealed partial class SettingsWindowViewModel : ObservableObject
{
    private readonly IAppSettingsStore _settingsStore;
    private readonly ISessionOrchestrator _sessionOrchestrator;
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly IAudioCaptureService _audioCaptureService;
    private readonly IReadOnlyList<TranscriptionProviderOption> _availableTranscriptionProviders;
    private readonly IReadOnlyList<AudioProcessingProviderOption> _availableAudioProcessingProviders;
    private readonly IReadOnlyList<OutputProviderOption> _availableOutputProviders;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ProviderSettingDescriptor>> _transcriptionProviderSettings;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ProviderSettingDescriptor>> _audioProcessingProviderSettings;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ProviderSettingDescriptor>> _outputProviderSettings;
    private readonly Dictionary<string, string> _providerSettingValues = new(StringComparer.OrdinalIgnoreCase);
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
    private string _startStopHotkey = string.Empty;

    [ObservableProperty]
    private string _cancelHotkey = string.Empty;

    [ObservableProperty]
    private string _selectedTranscriptionProviderId = string.Empty;

    [ObservableProperty]
    private string _selectedTranscriptionProviderDescription = string.Empty;

    [ObservableProperty]
    private string _selectedAudioProcessorProviderId = string.Empty;

    [ObservableProperty]
    private string _selectedAudioProcessorProviderDescription = string.Empty;

    [ObservableProperty]
    private bool _isCleanupEnabled;

    [ObservableProperty]
    private string _cleanupPrompt = string.Empty;

    [ObservableProperty]
    private string _selectedOutputProviderId = string.Empty;

    [ObservableProperty]
    private string _selectedOutputProviderDescription = string.Empty;

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
        IEnumerable<IBatchTranscriptionProvider> batchTranscriptionProviders,
        IEnumerable<IAudioProcessingProvider> audioProcessingProviders,
        IEnumerable<IOutputProvider> outputProviders)
    {
        var transcriptionProviderList = batchTranscriptionProviders.ToArray();
        var audioProcessingProviderList = audioProcessingProviders.ToArray();
        var outputProviderList = outputProviders.ToArray();

        _settingsStore = settingsStore;
        _sessionOrchestrator = sessionOrchestrator;
        _hotkeyService = hotkeyService;
        _audioCaptureService = audioCaptureService;
        _availableTranscriptionProviders = transcriptionProviderList
            .Select(provider => new TranscriptionProviderOption(provider.Metadata.Id, provider.Metadata.DisplayName, provider.Metadata.Description))
            .OrderBy(provider => provider.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        _availableAudioProcessingProviders = audioProcessingProviderList
            .Select(provider => new AudioProcessingProviderOption(provider.Metadata.Id, provider.Metadata.DisplayName, provider.Metadata.Description))
            .OrderBy(provider => provider.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        _availableOutputProviders = outputProviderList
            .Select(provider => new OutputProviderOption(provider.Id, provider.DisplayName, provider.Description))
            .OrderBy(provider => provider.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        _transcriptionProviderSettings = transcriptionProviderList.ToDictionary(
            provider => provider.Metadata.Id,
            provider => provider.SettingDescriptors,
            StringComparer.OrdinalIgnoreCase);
        _audioProcessingProviderSettings = audioProcessingProviderList.ToDictionary(
            provider => provider.Metadata.Id,
            provider => provider.SettingDescriptors,
            StringComparer.OrdinalIgnoreCase);
        _outputProviderSettings = outputProviderList.ToDictionary(
            provider => provider.Id,
            provider => provider.SettingDescriptors,
            StringComparer.OrdinalIgnoreCase);

        SessionState = _sessionOrchestrator.State.ToString();
        SessionStatus = _sessionOrchestrator.StatusMessage;
        HotkeyStatus = _hotkeyService.StatusMessage;

        _sessionOrchestrator.StateChanged += OnSessionStateChanged;
        _hotkeyService.RegistrationStatusChanged += OnHotkeyRegistrationStatusChanged;
        _audioCaptureService.LevelChanged += OnMicrophoneLevelChanged;

        LoadCommand = new AsyncRelayCommand(LoadAsync);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        RefreshMicrophonesCommand = new AsyncRelayCommand(RefreshMicrophonesAsync);
        OpenSettingsFolderCommand = new RelayCommand(OpenSettingsFolder);
    }

    public ObservableCollection<MicrophoneDeviceOption> MicrophoneDevices { get; } = new();

    public ObservableCollection<TranscriptionProviderOption> TranscriptionProviders { get; } = new();

    public ObservableCollection<AudioProcessingProviderOption> AudioProcessingProviders { get; } = new();

    public ObservableCollection<OutputProviderOption> OutputProviders { get; } = new();

    public ObservableCollection<ProviderSettingViewModel> TranscriptionProviderSettings { get; } = new();

    public ObservableCollection<ProviderSettingViewModel> CompactTranscriptionProviderSettings { get; } = new();

    public ObservableCollection<ProviderSettingViewModel> FullWidthTranscriptionProviderSettings { get; } = new();

    public ObservableCollection<ProviderSettingViewModel> AudioProcessingProviderSettings { get; } = new();

    public ObservableCollection<ProviderSettingViewModel> OutputProviderSettings { get; } = new();

    public string ConfigPath => _settingsStore.SettingsFilePath;

    public IAsyncRelayCommand LoadCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public IAsyncRelayCommand RefreshMicrophonesCommand { get; }

    public IRelayCommand OpenSettingsFolderCommand { get; }

    private async Task LoadAsync()
    {
        _settings = await _settingsStore.LoadAsync();
        var selectedMicrophoneDeviceId = _settings.SelectedMicrophoneDeviceId ?? string.Empty;

        ConfigVersion = _settings.ConfigVersion;
        LoadProviderSettingValuesFromSettings();
        LoadTranscriptionProviders();
        LoadAudioProcessingProviders();
        LoadOutputProviders();
        await RefreshMicrophonesAsync(selectedMicrophoneDeviceId);
        SelectedMicrophoneDeviceId = MicrophoneDevices.Any(device => device.Id == selectedMicrophoneDeviceId)
            ? selectedMicrophoneDeviceId
            : string.Empty;
        StartStopHotkey = _settings.StartStopHotkey.Gesture;
        CancelHotkey = _settings.CancelHotkey.Gesture;
        SelectedTranscriptionProviderId = ResolveSelectedTranscriptionProviderId(_settings.SelectedTranscriptionProviderId);
        SelectedAudioProcessorProviderId = ResolveSelectedAudioProcessorProviderId(_settings.SelectedAudioProcessorProviderId);
        IsCleanupEnabled = _settings.Cleanup.IsEnabled;
        CleanupPrompt = _settings.Cleanup.Prompt;
        SelectedOutputProviderId = ResolveSelectedOutputProviderId(_settings.EnabledOutputProviderIds.FirstOrDefault() ?? string.Empty);
        await StartLevelMonitoringAsync();
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

    private async Task StartLevelMonitoringAsync()
    {
        try
        {
            await _audioCaptureService.StartLevelMonitoringAsync(NormalizeMicrophoneDeviceId(SelectedMicrophoneDeviceId));
            IsLevelMonitoring = true;
            MicrophoneStatus = "Level meter active.";
        }
        catch (Exception exception)
        {
            IsLevelMonitoring = false;
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
            MicrophoneLevel = 0;
            MicrophoneLevelText = "0%";
        }
    }

    private async Task SaveAsync()
    {
        CaptureProviderSettingRows(TranscriptionProviderSettings);
        CaptureProviderSettingRows(CompactTranscriptionProviderSettings);
        CaptureProviderSettingRows(FullWidthTranscriptionProviderSettings);
        CaptureProviderSettingRows(AudioProcessingProviderSettings);
        CaptureProviderSettingRows(OutputProviderSettings);

        var nextSettings = new AppSettings
        {
            ConfigVersion = AppSettings.CurrentConfigVersion,
            SelectedMicrophoneDeviceId = string.IsNullOrWhiteSpace(SelectedMicrophoneDeviceId)
                ? null
                : SelectedMicrophoneDeviceId.Trim(),
            StartStopHotkey = HotkeySettings.FromGesture(StartStopHotkey.Trim()),
            CancelHotkey = HotkeySettings.FromGesture(CancelHotkey.Trim()),
            SelectedTranscriptionProviderId = ResolveSelectedTranscriptionProviderId(SelectedTranscriptionProviderId),
            SelectedAudioProcessorProviderId = ResolveSelectedAudioProcessorProviderId(SelectedAudioProcessorProviderId),
            Transcription = new TranscriptionSettings
            {
                WhisperCppModelId = GetProviderSettingValue(
                    ProviderSettingKeys.WhisperCppModelId,
                    WhisperCppModelCatalog.TinyEnglishModelId),
                WhisperCppExecutablePathOverride = _settings.Transcription.WhisperCppExecutablePathOverride,
                WhisperModelPathOverride = _settings.Transcription.WhisperModelPathOverride,
                Language = GetProviderSettingValue(ProviderSettingKeys.TranscriptionLanguage, _settings.Transcription.Language).Trim(),
                TimeoutSeconds = GetProviderSettingIntValue(
                    ProviderSettingKeys.TranscriptionTimeoutSeconds,
                    _settings.Transcription.TimeoutSeconds)
            },
            Cleanup = new CleanupSettings
            {
                IsEnabled = IsCleanupEnabled,
                ProviderId = _settings.Cleanup.ProviderId,
                Prompt = CleanupPrompt.Trim()
            },
            EnabledOutputProviderIds = new List<string> { ResolveSelectedOutputProviderId(SelectedOutputProviderId) },
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

    private void LoadAudioProcessingProviders()
    {
        AudioProcessingProviders.Clear();

        foreach (var provider in _availableAudioProcessingProviders)
        {
            AudioProcessingProviders.Add(provider);
        }
    }

    private void LoadOutputProviders()
    {
        OutputProviders.Clear();

        foreach (var provider in _availableOutputProviders)
        {
            OutputProviders.Add(provider);
        }
    }

    private void LoadProviderSettingValuesFromSettings()
    {
        _providerSettingValues.Clear();
        _providerSettingValues[ProviderSettingKeys.WhisperCppModelId] = ResolveWhisperCppModelId(_settings.Transcription.WhisperCppModelId);
        _providerSettingValues[ProviderSettingKeys.TranscriptionLanguage] = _settings.Transcription.Language;
        _providerSettingValues[ProviderSettingKeys.TranscriptionTimeoutSeconds] = _settings.Transcription.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
    }

    private void CaptureProviderSettingRows(IEnumerable<ProviderSettingViewModel> settingRows)
    {
        foreach (var row in settingRows)
        {
            _providerSettingValues[row.Key] = row.Value;
        }
    }

    private void LoadProviderSettingRows(
        ObservableCollection<ProviderSettingViewModel> settingRows,
        IReadOnlyDictionary<string, IReadOnlyList<ProviderSettingDescriptor>> providerSettings,
        string providerId)
    {
        settingRows.Clear();

        if (!providerSettings.TryGetValue(providerId, out var descriptors))
        {
            return;
        }

        foreach (var descriptor in descriptors)
        {
            settingRows.Add(new ProviderSettingViewModel(descriptor, GetInitialProviderSettingValue(descriptor)));
        }
    }

    private void LoadTranscriptionProviderSettingRows(string providerId)
    {
        TranscriptionProviderSettings.Clear();
        CompactTranscriptionProviderSettings.Clear();
        FullWidthTranscriptionProviderSettings.Clear();

        if (!_transcriptionProviderSettings.TryGetValue(providerId, out var descriptors))
        {
            return;
        }

        foreach (var descriptor in descriptors)
        {
            var row = new ProviderSettingViewModel(descriptor, GetInitialProviderSettingValue(descriptor));
            TranscriptionProviderSettings.Add(row);

            if (row.IsCompact)
            {
                CompactTranscriptionProviderSettings.Add(row);
                continue;
            }

            FullWidthTranscriptionProviderSettings.Add(row);
        }
    }

    private string GetInitialProviderSettingValue(ProviderSettingDescriptor descriptor)
    {
        if (_providerSettingValues.TryGetValue(descriptor.Key, out var value))
        {
            return value;
        }

        return descriptor.Options?.FirstOrDefault()?.Value ?? string.Empty;
    }

    private string GetProviderSettingValue(string key, string fallback)
    {
        return _providerSettingValues.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;
    }

    private int GetProviderSettingIntValue(string key, int fallback)
    {
        return int.TryParse(GetProviderSettingValue(key, fallback.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : fallback;
    }

    private static string ResolveWhisperCppModelId(string modelId)
    {
        return WhisperCppModelCatalog.Models.Any(model => model.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase))
            ? modelId
            : WhisperCppModelCatalog.TinyEnglishModelId;
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

    private string ResolveSelectedAudioProcessorProviderId(string providerId)
    {
        if (AudioProcessingProviders.Any(provider => provider.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase)))
        {
            return providerId;
        }

        if (AudioProcessingProviders.Any(provider => provider.Id.Equals(NoOpAudioProcessingProvider.ProviderId, StringComparison.OrdinalIgnoreCase)))
        {
            return NoOpAudioProcessingProvider.ProviderId;
        }

        return AudioProcessingProviders.FirstOrDefault()?.Id ?? providerId.Trim();
    }

    private string ResolveSelectedOutputProviderId(string providerId)
    {
        if (OutputProviders.Any(provider => provider.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase)))
        {
            return providerId;
        }

        if (OutputProviders.Any(provider => provider.Id.Equals(ClipboardOutputProvider.ProviderId, StringComparison.OrdinalIgnoreCase)))
        {
            return ClipboardOutputProvider.ProviderId;
        }

        return OutputProviders.FirstOrDefault()?.Id ?? providerId.Trim();
    }

    partial void OnSelectedTranscriptionProviderIdChanged(string value)
    {
        CaptureProviderSettingRows(TranscriptionProviderSettings);
        CaptureProviderSettingRows(CompactTranscriptionProviderSettings);
        CaptureProviderSettingRows(FullWidthTranscriptionProviderSettings);
        SelectedTranscriptionProviderDescription = TranscriptionProviders.FirstOrDefault(provider => provider.Id.Equals(value, StringComparison.OrdinalIgnoreCase))?.Description ?? string.Empty;
        LoadTranscriptionProviderSettingRows(value);
    }

    partial void OnSelectedAudioProcessorProviderIdChanged(string value)
    {
        CaptureProviderSettingRows(AudioProcessingProviderSettings);
        SelectedAudioProcessorProviderDescription = AudioProcessingProviders.FirstOrDefault(provider => provider.Id.Equals(value, StringComparison.OrdinalIgnoreCase))?.Description ?? string.Empty;
        LoadProviderSettingRows(AudioProcessingProviderSettings, _audioProcessingProviderSettings, value);
    }

    partial void OnSelectedOutputProviderIdChanged(string value)
    {
        CaptureProviderSettingRows(OutputProviderSettings);
        SelectedOutputProviderDescription = OutputProviders.FirstOrDefault(provider => provider.Id.Equals(value, StringComparison.OrdinalIgnoreCase))?.Description ?? string.Empty;
        LoadProviderSettingRows(OutputProviderSettings, _outputProviderSettings, value);
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
                MicrophoneStatus = "Recording level active.";
            }

            if (eventArgs.CurrentState == AppSessionState.Idle && !IsLevelMonitoring)
            {
                _ = StartLevelMonitoringAsync();
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
