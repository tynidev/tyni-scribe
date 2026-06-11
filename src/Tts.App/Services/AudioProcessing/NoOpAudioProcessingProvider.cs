namespace Tts.App.Services.AudioProcessing;

public sealed class NoOpAudioProcessingProvider : IAudioProcessingProvider
{
    public const string ProviderId = "noop";

    public AudioProcessingProviderMetadata Metadata { get; } = new(ProviderId, "No processing");

    public Task<AudioProcessingResult> ProcessAsync(AudioProcessingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new AudioProcessingResult(request.AudioFilePath, IsOriginalFile: true));
    }
}