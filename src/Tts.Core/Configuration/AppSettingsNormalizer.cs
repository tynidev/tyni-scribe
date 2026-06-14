using Tts.Core.Services.AudioProcessing;
using Tts.Core.Services.Output;
using Tts.Core.Services.Transcription;

namespace Tts.Core.Configuration;

public static class AppSettingsNormalizer
{
    public static AppSettings Normalize(AppSettings settings)
    {
        settings.ConfigVersion = AppSettings.CurrentConfigVersion;

        settings.StartStopHotkey ??= AppSettings.CreateDefaultStartStopHotkey();
        if (settings.StartStopHotkey.Gesture.Equals(AppSettings.PreviousDefaultStartStopHotkeyGesture, StringComparison.OrdinalIgnoreCase))
        {
            settings.StartStopHotkey = AppSettings.CreateDefaultStartStopHotkey();
        }

        settings.CancelHotkey ??= AppSettings.CreateDefaultCancelHotkey();
        if (settings.CancelHotkey.Gesture.Equals(AppSettings.PreviousDefaultCancelHotkeyGesture, StringComparison.OrdinalIgnoreCase))
        {
            settings.CancelHotkey = AppSettings.CreateDefaultCancelHotkey();
        }

        settings.SelectedTranscriptionProviderId = string.IsNullOrWhiteSpace(settings.SelectedTranscriptionProviderId)
            ? AppSettings.DefaultTranscriptionProviderId
            : settings.SelectedTranscriptionProviderId;
        if (settings.SelectedTranscriptionProviderId.Equals(WhisperCppBatchTranscriptionProvider.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            settings.SelectedTranscriptionProviderId = AppSettings.DefaultTranscriptionProviderId;
        }

        settings.SelectedAudioProcessorProviderId = string.IsNullOrWhiteSpace(settings.SelectedAudioProcessorProviderId)
            ? AppSettings.DefaultAudioProcessorProviderId
            : settings.SelectedAudioProcessorProviderId;
        settings.TranscriptionProviderSettings = NormalizeTranscriptionProviderSettings(settings.TranscriptionProviderSettings);
        settings.AudioProcessingProviderSettings = NormalizeProviderSettings(
            settings.AudioProcessingProviderSettings,
            AppSettings.DefaultAudioProcessorProviderId);
        settings.Cleanup ??= new CleanupSettings();
        settings.EnabledOutputProviderIds ??= AppSettings.CreateDefaultEnabledOutputProviderIds();
        if (settings.EnabledOutputProviderIds.Count == 0)
        {
            settings.EnabledOutputProviderIds.Add(AppSettings.DefaultOutputProviderId);
        }

        settings.OutputProviderSettings = NormalizeProviderSettings(
            settings.OutputProviderSettings,
            BuiltInOutputProviderIds.Clipboard,
            BuiltInOutputProviderIds.Paste);

        settings.SettingsWindow ??= new SettingsWindowPlacement();

        return settings;
    }

    private static Dictionary<string, Dictionary<string, string>> NormalizeTranscriptionProviderSettings(
        Dictionary<string, Dictionary<string, string>>? providerSettings)
    {
        return new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            [WhisperCppBatchTranscriptionProvider.ProviderId] = WhisperCppProviderSettings.Normalize(GetProviderSettings(providerSettings, WhisperCppBatchTranscriptionProvider.ProviderId)),
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