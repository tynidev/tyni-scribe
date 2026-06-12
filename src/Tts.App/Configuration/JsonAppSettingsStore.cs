using System.IO;
using System.Text.Json;
using Tts.App.Services;
using Tts.App.Services.AudioProcessing;
using Tts.App.Services.Output;
using Tts.App.Services.Transcription;

namespace Tts.App.Configuration;

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly SemaphoreSlim _sync = new(1, 1);

    public JsonAppSettingsStore(AppPaths paths)
    {
        _paths = paths;
    }

    public string SettingsFilePath => _paths.SettingsFilePath;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);

        try
        {
            Directory.CreateDirectory(_paths.AppDataDirectory);

            if (!File.Exists(SettingsFilePath))
            {
                var defaultSettings = Normalize(new AppSettings());
                await WriteSettingsAsync(defaultSettings, cancellationToken);
                return defaultSettings;
            }

            await using var stream = new FileStream(
                SettingsFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);

            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(
                stream,
                _serializerOptions,
                cancellationToken);

            return Normalize(settings ?? new AppSettings());
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);

        try
        {
            Directory.CreateDirectory(_paths.AppDataDirectory);
            await WriteSettingsAsync(Normalize(settings), cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task WriteSettingsAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var temporaryPath = SettingsFilePath + ".tmp";

        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 4096,
                         useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, settings, _serializerOptions, cancellationToken);
        }

        File.Move(temporaryPath, SettingsFilePath, overwrite: true);
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.ConfigVersion = AppSettings.CurrentConfigVersion;

        settings.StartStopHotkey ??= HotkeySettings.FromGesture("Ctrl+Alt+Space");
        settings.CancelHotkey ??= HotkeySettings.FromGesture("Ctrl+Shift+Space");
        if (settings.CancelHotkey.Gesture.Equals("Ctrl+Alt+Escape", StringComparison.OrdinalIgnoreCase))
        {
            settings.CancelHotkey = HotkeySettings.FromGesture("Ctrl+Shift+Space");
        }

        settings.SelectedTranscriptionProviderId = string.IsNullOrWhiteSpace(settings.SelectedTranscriptionProviderId)
            ? "whisper-cpp-native-local"
            : settings.SelectedTranscriptionProviderId;
        if (settings.SelectedTranscriptionProviderId.Equals("whisper-cpp-local", StringComparison.OrdinalIgnoreCase))
        {
            settings.SelectedTranscriptionProviderId = "whisper-cpp-native-local";
        }

        settings.SelectedAudioProcessorProviderId = string.IsNullOrWhiteSpace(settings.SelectedAudioProcessorProviderId)
            ? "noop"
            : settings.SelectedAudioProcessorProviderId;
        settings.TranscriptionProviderSettings = NormalizeTranscriptionProviderSettings(settings.TranscriptionProviderSettings);
        settings.AudioProcessingProviderSettings = NormalizeProviderSettings(
            settings.AudioProcessingProviderSettings,
            NoOpAudioProcessingProvider.ProviderId);
        settings.Cleanup ??= new CleanupSettings();
        settings.EnabledOutputProviderIds ??= new List<string> { "paste" };
        if (settings.EnabledOutputProviderIds.Count == 0)
        {
            settings.EnabledOutputProviderIds.Add("paste");
        }
        else if (settings.EnabledOutputProviderIds.Count == 1 && settings.EnabledOutputProviderIds[0].Equals("clipboard", StringComparison.OrdinalIgnoreCase))
        {
            settings.EnabledOutputProviderIds[0] = "paste";
        }

        settings.OutputProviderSettings = NormalizeProviderSettings(
            settings.OutputProviderSettings,
            ClipboardOutputProvider.ProviderId,
            PasteOutputProvider.ProviderId);

        settings.SettingsWindow ??= new SettingsWindowPlacement();

        return settings;
    }

    private static Dictionary<string, Dictionary<string, string>> NormalizeTranscriptionProviderSettings(
        Dictionary<string, Dictionary<string, string>>? providerSettings)
    {
        return new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            [WhisperCppBatchTranscriptionProvider.ProviderId] = WhisperCppProviderSettings.Normalize(GetProviderSettings(providerSettings, WhisperCppBatchTranscriptionProvider.ProviderId)),
            [FasterWhisperBatchTranscriptionProvider.ProviderId] = FasterWhisperProviderSettings.Normalize(GetProviderSettings(providerSettings, FasterWhisperBatchTranscriptionProvider.ProviderId)),
            [WhisperWarmBatchTranscriptionProvider.ProviderId] = WhisperWarmProviderSettings.Normalize(GetProviderSettings(providerSettings, WhisperWarmBatchTranscriptionProvider.ProviderId)),
            [WhisperNativeBatchTranscriptionProvider.ProviderId] = WhisperNativeProviderSettings.Normalize(GetProviderSettings(providerSettings, WhisperNativeBatchTranscriptionProvider.ProviderId))
        };
    }

    private static Dictionary<string, Dictionary<string, string>> NormalizeProviderSettings(
        Dictionary<string, Dictionary<string, string>>? providerSettings,
        params string[] providerIds)
    {
        var normalized = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var providerId in providerIds)
        {
            normalized[providerId] = CopyProviderSettings(GetProviderSettings(providerSettings, providerId));
        }

        return normalized;
    }

    private static Dictionary<string, string> CopyProviderSettings(IReadOnlyDictionary<string, string>? providerSettings)
    {
        return providerSettings is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(providerSettings, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string>? GetProviderSettings(
        Dictionary<string, Dictionary<string, string>>? providerSettings,
        string providerId)
    {
        if (providerSettings is null)
        {
            return null;
        }

        foreach (var pair in providerSettings)
        {
            if (pair.Key.Equals(providerId, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }
}