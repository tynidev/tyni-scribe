namespace Tts.Cli.Transcription;

internal sealed record TranscriptionMetrics(
    string Status,
    string? ErrorCategory,
    string? ProviderId,
    string? AudioProcessorProviderId,
    IReadOnlyDictionary<string, string> EffectiveSettings,
    string? AudioFilePath,
    double? AudioDurationSeconds,
    long? AudioProcessingMilliseconds,
    long? TranscriptionMilliseconds,
    double? TranscriptionRealTimeFactor,
    double? TranscriptionAudioSecondsPerSecond,
    long TotalMilliseconds);