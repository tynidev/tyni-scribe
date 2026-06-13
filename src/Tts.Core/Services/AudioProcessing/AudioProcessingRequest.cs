using Tts.Core.Services.Audio;

namespace Tts.Core.Services.AudioProcessing;

public sealed record AudioProcessingRequest(
    string AudioFilePath,
    AudioCaptureFormat AudioFormat,
    TimeSpan Duration,
    IReadOnlyDictionary<string, string> Settings);