namespace Tts.App.Services.Transcription;

internal enum FasterWhisperNativeInteropStatus
{
    Ok = 0,
    Canceled = 1,
    ModelNotFound = 2,
    ModelLoadFailed = 3,
    InvalidAudio = 4,
    TranscriptionFailed = 5,
    NotInitialized = 6,
    InvalidArgument = 7,
    Timeout = 8,
    DependencyUnavailable = 9,
    NotImplemented = 10,
    NativeFailure = 100
}