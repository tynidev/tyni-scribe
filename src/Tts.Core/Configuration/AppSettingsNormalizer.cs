using Tts.Core.Services.AudioProcessing;
using Tts.Core.Services.Output;
using Tts.Core.Services.Transcription;

namespace Tts.Core.Configuration;

public static class AppSettingsNormalizer
{
    public static AppSettings Normalize(AppSettings settings)
    {
        settings.ConfigVersion = AppSettings.CurrentConfigVersion;

        settings.StartStopHotkey ??= HotkeySettings.FromGesture("Ctrl+Alt+Space");
        settings.CancelHotkey ??= HotkeySettings.FromGesture("Ctrl+Shift+Space");
        if (settings.CancelHotkey.Gesture.Equals("Ctrl+Alt+Escape", StringComparison.OrdinalIgnoreCase))
        {
            settings.CancelHotkey = HotkeySettings.FromGesture("Ctrl+Shift+Space");
        }

        settings.SelectedTranscriptionProviderId = string.IsNullOrWhiteSpace(settings.SelectedTranscriptionProviderId)
            ? WhisperNativeBatchTranscriptionProvider.ProviderId
            : settings.SelectedTranscriptionProviderId;
        if (settings.SelectedTranscriptionProviderId.Equals(WhisperCppBatchTranscriptionProvider.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            settings.SelectedTranscriptionProviderId = WhisperNativeBatchTranscriptionProvider.ProviderId;
        }

        settings.SelectedAudioProcessorProviderId = string.IsNullOrWhiteSpace(settings.SelectedAudioProcessorProviderId)
            ? NoOpAudioProcessingProvider.ProviderId
            : settings.SelectedAudioProcessorProviderId;
        settings.TranscriptionProviderSettings = NormalizeTranscriptionProviderSettings(settings.TranscriptionProviderSettings);
        settings.AudioProcessingProviderSettings = NormalizeProviderSettings(
            settings.AudioProcessingProviderSettings,
            NoOpAudioProcessingProvider.ProviderId);
        settings.Cleanup ??= new CleanupSettings();
        settings.EnabledOutputProviderIds ??= new List<string> { BuiltInOutputProviderIds.Paste };
        if (settings.EnabledOutputProviderIds.Count == 0)
        {
            settings.EnabledOutputProviderIds.Add(BuiltInOutputProviderIds.Paste);
        }
        else if (settings.EnabledOutputProviderIds.Count == 1 && settings.EnabledOutputProviderIds[0].Equals(BuiltInOutputProviderIds.Clipboard, StringComparison.OrdinalIgnoreCase))
        {
            settings.EnabledOutputProviderIds[0] = BuiltInOutputProviderIds.Paste;
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