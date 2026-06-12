namespace Tts.App.Services.AudioProcessing;

public interface IAudioProcessingProvider
{
    AudioProcessingProviderMetadata Metadata { get; }

    IReadOnlyList<global::Tts.App.Services.ProviderSettingDescriptor> SettingDescriptors { get; }

    Task<AudioProcessingResult> ProcessAsync(AudioProcessingRequest request, CancellationToken cancellationToken = default);
}
