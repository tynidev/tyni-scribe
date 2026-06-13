namespace Tts.Core.Services.Transcription;

public sealed record WhisperNativeTranscriptionRequest(
    string ModelId,
    string ModelPath,
    string AudioFilePath,
    string Language,
    int TimeoutSeconds);