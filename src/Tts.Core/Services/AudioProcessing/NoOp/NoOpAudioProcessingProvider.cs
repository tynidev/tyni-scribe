using Tts.Core.Services;

namespace Tts.Core.Services.AudioProcessing;

public sealed class NoOpAudioProcessingProvider : IAudioProcessingProvider
{
    public const string ProviderId = "noop";

    public AudioProcessingProviderMetadata Metadata { get; } = new(
        ProviderId,
        "None",
        "Leaves completed recordings unchanged before transcription.");

    public IReadOnlyList<ProviderSettingDescriptor> SettingDescriptors { get; } = Array.Empty<ProviderSettingDescriptor>();

    public Task<AudioProcessingResult> ProcessAsync(AudioProcessingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new AudioProcessingResult(request.AudioFilePath, IsOriginalFile: true));
    }
}
