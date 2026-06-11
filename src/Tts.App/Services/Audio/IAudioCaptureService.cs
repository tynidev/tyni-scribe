namespace Tts.App.Services.Audio;

public interface IAudioCaptureService
{
    event EventHandler<MicrophoneLevelChangedEventArgs>? LevelChanged;

    event EventHandler<AudioChunkCapturedEventArgs>? AudioChunkCaptured;

    bool IsRecording { get; }

    bool IsLevelMonitoring { get; }

    double CurrentLevel { get; }

    Task<IReadOnlyList<MicrophoneDevice>> EnumerateMicrophonesAsync(CancellationToken cancellationToken = default);

    Task StartLevelMonitoringAsync(string? microphoneDeviceId, CancellationToken cancellationToken = default);

    Task StopLevelMonitoringAsync(CancellationToken cancellationToken = default);

    Task StartRecordingAsync(string? microphoneDeviceId, CancellationToken cancellationToken = default);

    Task<AudioRecordingResult> StopRecordingAsync(CancellationToken cancellationToken = default);

    Task CancelRecordingAsync(CancellationToken cancellationToken = default);
}