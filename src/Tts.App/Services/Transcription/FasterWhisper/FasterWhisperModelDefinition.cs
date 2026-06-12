namespace Tts.App.Services.Transcription;

public sealed record FasterWhisperModelDefinition(
    string Id,
    string DisplayName,
    string DirectoryName,
    string HuggingFaceModelId);