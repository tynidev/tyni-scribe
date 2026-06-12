namespace Tts.App.Services;

public sealed record SessionSnapshot(
    string? MicrophoneDeviceId,
    string TranscriptionProviderId,
    string AudioProcessorProviderId,
    IReadOnlyDictionary<string, string> TranscriptionProviderSettings,
    IReadOnlyDictionary<string, string> AudioProcessingProviderSettings,
    IReadOnlyList<string> EnabledOutputProviderIds,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> OutputProviderSettings,
    bool IsCleanupEnabled,
    string CleanupProviderId,
    string CleanupPrompt);