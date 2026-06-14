using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tts.App.Services;
using Tts.App.Services.Output;
using Tts.Core.Configuration;
using Tts.Core.Services;
using Tts.Core.Services.Audio;
using Tts.Core.Services.AudioProcessing;
using Tts.Core.Services.Output;
using Tts.Core.Services.Transcription;

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
    private readonly Dictionary<string, Dictionary<string, string>> _transcriptionProviderSettingValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _audioProcessingProviderSettingValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _outputProviderSettingValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private CancellationTokenSource? _autoSaveCancellation;
    private AppSettings _settings = new();
    private string _currentTranscriptionProviderSettingsProviderId = string.Empty;
    private string _currentAudioProcessingProviderSettingsProviderId = string.Empty;
    private string _currentOutputProviderSettingsProviderId = string.Empty;
    private bool _isRefreshingMicrophones;
    private bool _isLoaded;

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
        _isLoaded = false;
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
        _isLoaded = true;
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

    private Task SaveAsync()
    {
        return SaveSettingsAsync(CancellationToken.None);
    }

    private async Task SaveSettingsAsync(CancellationToken cancellationToken)
    {
        await _saveLock.WaitAsync(cancellationToken);

        try
        {
        CaptureCurrentTranscriptionProviderSettingRows();
        CaptureCurrentAudioProcessingProviderSettingRows();
        CaptureCurrentOutputProviderSettingRows();

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
            TranscriptionProviderSettings = CopyTranscriptionProviderSettings(),
            AudioProcessingProviderSettings = CopyProviderSettings(_audioProcessingProviderSettingValues),
            Cleanup = new CleanupSettings
            {
                IsEnabled = IsCleanupEnabled,
                ProviderId = _settings.Cleanup.ProviderId,
                Prompt = CleanupPrompt.Trim()
            },
            EnabledOutputProviderIds = new List<string> { ResolveSelectedOutputProviderId(SelectedOutputProviderId) },
            OutputProviderSettings = CopyProviderSettings(_outputProviderSettingValues),
            SettingsWindow = _settings.SettingsWindow
        };

        var hotkeyResult = await _hotkeyService.ApplySettingsAsync(nextSettings, cancellationToken);

        if (!hotkeyResult.Succeeded)
        {
            StatusMessage = hotkeyResult.Message;
            return;
        }

        _settings = nextSettings;
        await _settingsStore.SaveAsync(_settings, cancellationToken);
        StatusMessage = "Settings saved";
        }
        finally
        {
            _saveLock.Release();
        }
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
        _currentTranscriptionProviderSettingsProviderId = string.Empty;
        _currentAudioProcessingProviderSettingsProviderId = string.Empty;
        _currentOutputProviderSettingsProviderId = string.Empty;
        _transcriptionProviderSettingValues.Clear();
        _audioProcessingProviderSettingValues.Clear();
        _outputProviderSettingValues.Clear();

        foreach (var providerSettings in _settings.TranscriptionProviderSettings)
        {
            _transcriptionProviderSettingValues[providerSettings.Key] = new Dictionary<string, string>(providerSettings.Value, StringComparer.OrdinalIgnoreCase);
        }

        foreach (var providerSettings in _settings.AudioProcessingProviderSettings)
        {
            _audioProcessingProviderSettingValues[providerSettings.Key] = new Dictionary<string, string>(providerSettings.Value, StringComparer.OrdinalIgnoreCase);
        }

        foreach (var providerSettings in _settings.OutputProviderSettings)
        {
            _outputProviderSettingValues[providerSettings.Key] = new Dictionary<string, string>(providerSettings.Value, StringComparer.OrdinalIgnoreCase);
        }
    }

    private void CaptureCurrentTranscriptionProviderSettingRows()
    {
        if (string.IsNullOrWhiteSpace(_currentTranscriptionProviderSettingsProviderId))
        {
            return;
        }

        CaptureTranscriptionProviderSettingRows(_currentTranscriptionProviderSettingsProviderId, TranscriptionProviderSettings);
    }

    private void CaptureTranscriptionProviderSettingRows(string providerId, IEnumerable<ProviderSettingViewModel> settingRows)
    {
        var providerSettings = GetOrCreateTranscriptionProviderSettingValues(providerId);

        foreach (var row in settingRows)
        {
            providerSettings[row.Key] = row.Value;
        }
    }

    private void CaptureCurrentAudioProcessingProviderSettingRows()
    {
        CaptureCurrentProviderSettingRows(_currentAudioProcessingProviderSettingsProviderId, AudioProcessingProviderSettings, _audioProcessingProviderSettingValues);
    }

    private void CaptureCurrentOutputProviderSettingRows()
    {
        CaptureCurrentProviderSettingRows(_currentOutputProviderSettingsProviderId, OutputProviderSettings, _outputProviderSettingValues);
    }

    private static void CaptureCurrentProviderSettingRows(
        string providerId,
        IEnumerable<ProviderSettingViewModel> settingRows,
        Dictionary<string, Dictionary<string, string>> providerSettingValues)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return;
        }

        var providerSettings = GetOrCreateProviderSettingValues(providerSettingValues, providerId);

        foreach (var row in settingRows)
        {
            providerSettings[row.Key] = row.Value;
        }
    }

    private void LoadProviderSettingRows(
        ObservableCollection<ProviderSettingViewModel> settingRows,
        IReadOnlyDictionary<string, IReadOnlyList<ProviderSettingDescriptor>> providerSettings,
        string providerId,
        Dictionary<string, Dictionary<string, string>> providerSettingValues)
    {
        ClearProviderSettingRows(settingRows);

        if (!providerSettings.TryGetValue(providerId, out var descriptors))
        {
            return;
        }

        foreach (var descriptor in descriptors)
        {
            settingRows.Add(CreateProviderSettingRow(providerId, descriptor, providerSettingValues));
        }
    }

    private void LoadTranscriptionProviderSettingRows(string providerId)
    {
        ClearProviderSettingRows(TranscriptionProviderSettings);
        CompactTranscriptionProviderSettings.Clear();
        FullWidthTranscriptionProviderSettings.Clear();
        _currentTranscriptionProviderSettingsProviderId = providerId;

        if (!_transcriptionProviderSettings.TryGetValue(providerId, out var descriptors))
        {
            return;
        }

        foreach (var descriptor in descriptors)
        {
            var row = CreateTranscriptionProviderSettingRow(providerId, descriptor);
            TranscriptionProviderSettings.Add(row);

            if (row.IsCompact)
            {
                CompactTranscriptionProviderSettings.Add(row);
                continue;
            }

            FullWidthTranscriptionProviderSettings.Add(row);
        }
    }

    private ProviderSettingViewModel CreateProviderSettingRow(
        string providerId,
        ProviderSettingDescriptor descriptor,
        Dictionary<string, Dictionary<string, string>> providerSettingValues)
    {
        var row = new ProviderSettingViewModel(descriptor, GetInitialProviderSettingValue(providerId, descriptor, providerSettingValues));
        row.PropertyChanged += OnProviderSettingRowPropertyChanged;
        return row;
    }

    private ProviderSettingViewModel CreateTranscriptionProviderSettingRow(string providerId, ProviderSettingDescriptor descriptor)
    {
        var row = new ProviderSettingViewModel(descriptor, GetInitialTranscriptionProviderSettingValue(providerId, descriptor));
        row.PropertyChanged += OnProviderSettingRowPropertyChanged;
        return row;
    }

    private void ClearProviderSettingRows(ObservableCollection<ProviderSettingViewModel> settingRows)
    {
        foreach (var row in settingRows)
        {
            row.PropertyChanged -= OnProviderSettingRowPropertyChanged;
        }

        settingRows.Clear();
    }

    private void OnProviderSettingRowPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(ProviderSettingViewModel.Value))
        {
            QueueAutoSave();
        }
    }

    private static string GetInitialProviderSettingValue(
        string providerId,
        ProviderSettingDescriptor descriptor,
        Dictionary<string, Dictionary<string, string>> providerSettingValues)
    {
        var providerSettings = GetOrCreateProviderSettingValues(providerSettingValues, providerId);

        if (providerSettings.TryGetValue(descriptor.Key, out var value))
        {
            return value;
        }

        return descriptor.Options?.FirstOrDefault()?.Value ?? string.Empty;
    }

    private string GetInitialTranscriptionProviderSettingValue(string providerId, ProviderSettingDescriptor descriptor)
    {
        var providerSettings = GetOrCreateTranscriptionProviderSettingValues(providerId);

        if (providerSettings.TryGetValue(descriptor.Key, out var value))
        {
            return value;
        }

        return descriptor.Options?.FirstOrDefault()?.Value ?? string.Empty;
    }

    private Dictionary<string, string> GetOrCreateTranscriptionProviderSettingValues(string providerId)
    {
        if (_transcriptionProviderSettingValues.TryGetValue(providerId, out var providerSettings))
        {
            return providerSettings;
        }

        providerSettings = CreateDefaultTranscriptionProviderSettingValues(providerId);
        _transcriptionProviderSettingValues[providerId] = providerSettings;
        return providerSettings;
    }

    private static Dictionary<string, string> GetOrCreateProviderSettingValues(
        Dictionary<string, Dictionary<string, string>> providerSettingValues,
        string providerId)
    {
        if (providerSettingValues.TryGetValue(providerId, out var providerSettings))
        {
            return providerSettings;
        }

        providerSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        providerSettingValues[providerId] = providerSettings;
        return providerSettings;
    }

    private static Dictionary<string, string> CreateDefaultTranscriptionProviderSettingValues(string providerId)
    {
        if (providerId.Equals(WhisperCppBatchTranscriptionProvider.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return WhisperCppProviderSettings.CreateDefaultValues();
        }

        if (providerId.Equals(WhisperNativeBatchTranscriptionProvider.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return WhisperNativeProviderSettings.CreateDefaultValues();
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, Dictionary<string, string>> CopyTranscriptionProviderSettings()
    {
        return CopyProviderSettings(_transcriptionProviderSettingValues);
    }

    private static Dictionary<string, Dictionary<string, string>> CopyProviderSettings(
        Dictionary<string, Dictionary<string, string>> providerSettingValues)
    {
        var copy = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var providerSettings in providerSettingValues)
        {
            copy[providerSettings.Key] = new Dictionary<string, string>(providerSettings.Value, StringComparer.OrdinalIgnoreCase);
        }

        return copy;
    }

    private string ResolveSelectedTranscriptionProviderId(string providerId)
    {
        if (TranscriptionProviders.Any(provider => provider.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase)))
        {
            return providerId;
        }

        if (TranscriptionProviders.Any(provider => provider.Id.Equals(AppSettings.DefaultTranscriptionProviderId, StringComparison.OrdinalIgnoreCase)))
        {
            return AppSettings.DefaultTranscriptionProviderId;
        }

        return TranscriptionProviders.FirstOrDefault()?.Id ?? providerId.Trim();
    }

    private string ResolveSelectedAudioProcessorProviderId(string providerId)
    {
        if (AudioProcessingProviders.Any(provider => provider.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase)))
        {
            return providerId;
        }

        if (AudioProcessingProviders.Any(provider => provider.Id.Equals(AppSettings.DefaultAudioProcessorProviderId, StringComparison.OrdinalIgnoreCase)))
        {
            return AppSettings.DefaultAudioProcessorProviderId;
        }

        return AudioProcessingProviders.FirstOrDefault()?.Id ?? providerId.Trim();
    }

    private string ResolveSelectedOutputProviderId(string providerId)
    {
        if (OutputProviders.Any(provider => provider.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase)))
        {
            return providerId;
        }

        if (OutputProviders.Any(provider => provider.Id.Equals(AppSettings.DefaultOutputProviderId, StringComparison.OrdinalIgnoreCase)))
        {
            return AppSettings.DefaultOutputProviderId;
        }

        return OutputProviders.FirstOrDefault()?.Id ?? providerId.Trim();
    }

    partial void OnSelectedTranscriptionProviderIdChanged(string value)
    {
        CaptureCurrentTranscriptionProviderSettingRows();
        SelectedTranscriptionProviderDescription = TranscriptionProviders.FirstOrDefault(provider => provider.Id.Equals(value, StringComparison.OrdinalIgnoreCase))?.Description ?? string.Empty;
        LoadTranscriptionProviderSettingRows(value);
        QueueAutoSave();
    }

    partial void OnSelectedAudioProcessorProviderIdChanged(string value)
    {
        CaptureCurrentAudioProcessingProviderSettingRows();
        SelectedAudioProcessorProviderDescription = AudioProcessingProviders.FirstOrDefault(provider => provider.Id.Equals(value, StringComparison.OrdinalIgnoreCase))?.Description ?? string.Empty;
        _currentAudioProcessingProviderSettingsProviderId = value;
        LoadProviderSettingRows(AudioProcessingProviderSettings, _audioProcessingProviderSettings, value, _audioProcessingProviderSettingValues);
        QueueAutoSave();
    }

    partial void OnSelectedOutputProviderIdChanged(string value)
    {
        CaptureCurrentOutputProviderSettingRows();
        SelectedOutputProviderDescription = OutputProviders.FirstOrDefault(provider => provider.Id.Equals(value, StringComparison.OrdinalIgnoreCase))?.Description ?? string.Empty;
        _currentOutputProviderSettingsProviderId = value;
        LoadProviderSettingRows(OutputProviderSettings, _outputProviderSettings, value, _outputProviderSettingValues);
        QueueAutoSave();
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

        QueueAutoSave();
    }

    partial void OnStartStopHotkeyChanged(string value)
    {
        QueueAutoSave();
    }

    partial void OnCancelHotkeyChanged(string value)
    {
        QueueAutoSave();
    }

    partial void OnIsCleanupEnabledChanged(bool value)
    {
        QueueAutoSave();
    }

    partial void OnCleanupPromptChanged(string value)
    {
        QueueAutoSave();
    }

    private void QueueAutoSave()
    {
        if (!_isLoaded)
        {
            return;
        }

        _autoSaveCancellation?.Cancel();
        _autoSaveCancellation?.Dispose();
        _autoSaveCancellation = new CancellationTokenSource();
        var cancellationToken = _autoSaveCancellation.Token;

        _ = AutoSaveAfterDelayAsync(cancellationToken);
    }

    private async Task AutoSaveAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            var operation = System.Windows.Application.Current.Dispatcher.InvokeAsync(() => SaveSettingsAsync(CancellationToken.None));
            await await operation.Task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not save settings: {exception.Message}";
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
