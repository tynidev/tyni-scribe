namespace Tts.App.Services;

public sealed record SessionSnapshot(
    string? MicrophoneDeviceId,
    string TranscriptionProviderId,
    IReadOnlyList<string> EnabledOutputProviderIds,
    bool IsCleanupEnabled,
    string CleanupProviderId,
    string CleanupPrompt);