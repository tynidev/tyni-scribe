using Tts.App.Services.Audio;

namespace Tts.App.Services.AudioProcessing;

public sealed record AudioProcessingRequest(
    string AudioFilePath,
    AudioCaptureFormat AudioFormat,
    TimeSpan Duration,
    IReadOnlyDictionary<string, string> Settings);