namespace Tts.Core.Services.Transcription;

public interface IBatchTranscriptionProvider
{
    TranscriptionProviderMetadata Metadata { get; }

    IReadOnlyList<global::Tts.Core.Services.ProviderSettingDescriptor> SettingDescriptors { get; }

    Task<BatchTranscriptionResult> TranscribeAsync(BatchTranscriptionRequest request, CancellationToken cancellationToken = default);
}
