using Tts.App.Configuration;
using Tts.App.Services.Audio;

namespace Tts.App.Services.Transcription;

public sealed record BatchTranscriptionRequest(
    string AudioFilePath,
    AudioCaptureFormat AudioFormat,
    TimeSpan Duration,
    TranscriptionSettings Settings);