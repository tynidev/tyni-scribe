namespace Tts.Core.Services.Audio;

public sealed record AudioRecordingResult(
    string FilePath,
    TimeSpan Duration,
    long DataBytesWritten,
    AudioCaptureFormat Format);