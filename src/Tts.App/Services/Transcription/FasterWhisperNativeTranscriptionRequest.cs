namespace Tts.App.Services.Transcription;

public sealed record FasterWhisperNativeTranscriptionRequest(
    string ModelId,
    string ModelDirectory,
    string AudioFilePath,
    string Language,
    string ComputeType,
    int TimeoutSeconds);