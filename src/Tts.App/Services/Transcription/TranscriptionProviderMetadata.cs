namespace Tts.App.Services.Transcription;

public sealed record TranscriptionProviderMetadata(
    string Id,
    string DisplayName,
    TranscriptionMode TranscriptionMode,
    bool RequiresEndpoint,
    string Description = "");
