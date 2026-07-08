using Tts.Core.Services.Audio;

namespace Tts.Core.Services.Media;

public interface IMediaPreparationService
{
    Task<MediaPreparationResult> PrepareAsync(MediaPreparationRequest request, CancellationToken cancellationToken = default);
}

public sealed record MediaPreparationRequest(
    string InputFilePath,
    string? OutputDirectory = null,
    int TimeoutSeconds = 600);

public sealed record MediaPreparationResult(
    string AudioFilePath,
    bool IsOriginalFile,
    AudioCaptureFormat Format,
    TimeSpan Duration);