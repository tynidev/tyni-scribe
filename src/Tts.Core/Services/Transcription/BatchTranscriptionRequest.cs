using Tts.Core.Services.Audio;

namespace Tts.Core.Services.Transcription;

public sealed record BatchTranscriptionRequest(
    string AudioFilePath,
    AudioCaptureFormat AudioFormat,
    TimeSpan Duration,
    IReadOnlyDictionary<string, string> Settings);