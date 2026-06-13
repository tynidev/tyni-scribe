namespace Tts.Core.Services.AudioProcessing;

public interface IAudioProcessingProvider
{
    AudioProcessingProviderMetadata Metadata { get; }

    IReadOnlyList<global::Tts.Core.Services.ProviderSettingDescriptor> SettingDescriptors { get; }

    Task<AudioProcessingResult> ProcessAsync(AudioProcessingRequest request, CancellationToken cancellationToken = default);
}
