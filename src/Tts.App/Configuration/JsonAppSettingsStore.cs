using System.IO;
using System.Text.Json;
using Tts.App.Services;
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
                var defaultSettings = new AppSettings();
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
        if (settings.ConfigVersion <= 0)
        {
            settings.ConfigVersion = AppSettings.CurrentConfigVersion;
        }

        settings.StartStopHotkey ??= HotkeySettings.FromGesture("Ctrl+Alt+Space");
        settings.CancelHotkey ??= HotkeySettings.FromGesture("Ctrl+Alt+Escape");
        settings.SelectedTranscriptionProviderId = string.IsNullOrWhiteSpace(settings.SelectedTranscriptionProviderId)
            ? "whisper-cpp-local"
            : settings.SelectedTranscriptionProviderId;
        settings.SelectedAudioProcessorProviderId = string.IsNullOrWhiteSpace(settings.SelectedAudioProcessorProviderId)
            ? "noop"
            : settings.SelectedAudioProcessorProviderId;
        settings.Transcription ??= new TranscriptionSettings();
        settings.Transcription.WhisperCppModelId = string.IsNullOrWhiteSpace(settings.Transcription.WhisperCppModelId)
            ? WhisperCppModelCatalog.TinyEnglishModelId
            : settings.Transcription.WhisperCppModelId;
        settings.Transcription.WhisperCppModelId = WhisperCppModelCatalog.Models.Any(model => model.Id.Equals(settings.Transcription.WhisperCppModelId, StringComparison.OrdinalIgnoreCase))
            ? settings.Transcription.WhisperCppModelId
            : WhisperCppModelCatalog.TinyEnglishModelId;
        settings.Transcription.WhisperCppExecutablePathOverride = string.IsNullOrWhiteSpace(settings.Transcription.WhisperCppExecutablePathOverride)
            ? null
            : settings.Transcription.WhisperCppExecutablePathOverride;
        settings.Transcription.WhisperModelPathOverride = string.IsNullOrWhiteSpace(settings.Transcription.WhisperModelPathOverride)
            ? null
            : settings.Transcription.WhisperModelPathOverride;
        settings.Transcription.Language = string.IsNullOrWhiteSpace(settings.Transcription.Language)
            ? "en"
            : settings.Transcription.Language;
        settings.Transcription.TimeoutSeconds = settings.Transcription.TimeoutSeconds <= 0
            ? 600
            : settings.Transcription.TimeoutSeconds;
        settings.Cleanup ??= new CleanupSettings();
        settings.EnabledOutputProviderIds ??= new List<string> { "clipboard" };
        settings.SettingsWindow ??= new SettingsWindowPlacement();

        return settings;
    }
}