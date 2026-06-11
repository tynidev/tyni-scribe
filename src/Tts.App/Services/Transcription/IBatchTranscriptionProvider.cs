namespace Tts.App.Services.Transcription;

public interface IBatchTranscriptionProvider
{
    TranscriptionProviderMetadata Metadata { get; }

    Task<BatchTranscriptionResult> TranscribeAsync(BatchTranscriptionRequest request, CancellationToken cancellationToken = default);
}