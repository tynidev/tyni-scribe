using Tts.App.Services.Audio;

namespace Tts.App.Services.Transcription;

public sealed record BatchTranscriptionRequest(
    string AudioFilePath,
    AudioCaptureFormat AudioFormat,
    TimeSpan Duration,
    IReadOnlyDictionary<string, string> Settings);