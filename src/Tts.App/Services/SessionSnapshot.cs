using Tts.App.Configuration;

namespace Tts.App.Services;

public sealed record SessionSnapshot(
    string? MicrophoneDeviceId,
    string TranscriptionProviderId,
    string AudioProcessorProviderId,
    TranscriptionSettings TranscriptionSettings,
    IReadOnlyList<string> EnabledOutputProviderIds,
    bool IsCleanupEnabled,
    string CleanupProviderId,
    string CleanupPrompt);