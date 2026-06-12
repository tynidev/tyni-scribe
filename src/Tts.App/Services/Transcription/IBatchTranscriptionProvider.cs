namespace Tts.App.Services.Transcription;

public interface IBatchTranscriptionProvider
{
    TranscriptionProviderMetadata Metadata { get; }

    IReadOnlyList<global::Tts.App.Services.ProviderSettingDescriptor> SettingDescriptors { get; }

    Task<BatchTranscriptionResult> TranscribeAsync(BatchTranscriptionRequest request, CancellationToken cancellationToken = default);
}
