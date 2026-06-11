namespace Tts.App.Services.AudioProcessing;

public interface IAudioProcessingProvider
{
    AudioProcessingProviderMetadata Metadata { get; }

    Task<AudioProcessingResult> ProcessAsync(AudioProcessingRequest request, CancellationToken cancellationToken = default);
}