namespace Tts.App.Services.Transcription;

internal enum WhisperNativeInteropStatus
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
    NativeFailure = 100
}