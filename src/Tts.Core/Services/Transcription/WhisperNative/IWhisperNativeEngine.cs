namespace Tts.Core.Services.Transcription;

public interface IWhisperNativeEngine : IDisposable
{
    Task<string> TranscribeAsync(WhisperNativeTranscriptionRequest request, CancellationToken cancellationToken = default);
}