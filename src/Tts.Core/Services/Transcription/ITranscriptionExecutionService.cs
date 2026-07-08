using Tts.Core.Configuration;

namespace Tts.Core.Services.Transcription;

public interface ITranscriptionExecutionService
{
    Task<TranscriptionExecutionResult> TranscribeAsync(TranscriptionExecutionRequest request, CancellationToken cancellationToken = default);
}

public sealed record TranscriptionExecutionRequest(
    string AudioFilePath,
    AppSettings Settings,
    string? ProviderId = null,
    string? AudioProcessorProviderId = null,
    IReadOnlyDictionary<string, string>? SettingOverrides = null);

public sealed record TranscriptionExecutionResult(
    string Text,
    string ProviderId,
    string AudioProcessorProviderId,
    IReadOnlyDictionary<string, string> EffectiveSettings,
    string AudioFilePath,
    double AudioDurationSeconds,
    long AudioProcessingMilliseconds,
    long TranscriptionMilliseconds,
    double? TranscriptionRealTimeFactor,
    double? TranscriptionAudioSecondsPerSecond);