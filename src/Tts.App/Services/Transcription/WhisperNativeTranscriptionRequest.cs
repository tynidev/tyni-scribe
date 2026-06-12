namespace Tts.App.Services.Transcription;

public sealed record WhisperNativeTranscriptionRequest(
    string ModelId,
    string ModelPath,
    string AudioFilePath,
    string Language,
    int TimeoutSeconds);