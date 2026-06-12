namespace Tts.App.Services.Timing;

public sealed record SessionTimingLogEntry(
    int SchemaVersion,
    Guid SessionId,
    DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc,
    string Status,
    string? ErrorCategory,
    string? MicrophoneDeviceId,
    string TranscriptionProviderId,
    string? AudioProcessorProviderId,
    string? CleanupProviderId,
    IReadOnlyList<string> OutputProviderIds,
    TimeSpan? RecordingDuration,
    TimeSpan TotalSessionDuration,
    TimeSpan? CaptureFinalizationDuration,
    TimeSpan? AudioProcessingDuration,
    TimeSpan? TranscriptionDuration,
    TimeSpan? TextCleanupDuration,
    TimeSpan? ClipboardOutputDuration,
    TimeSpan? TempFileCleanupDuration,
    string ProviderSettingsJson);