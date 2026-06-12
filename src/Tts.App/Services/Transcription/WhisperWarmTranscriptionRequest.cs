namespace Tts.App.Services.Transcription;

public sealed record WhisperWarmTranscriptionRequest(
    string ModelId,
    string ModelPath,
    string ServerExecutablePath,
    string AudioFilePath,
    string Language,
    int TimeoutSeconds);