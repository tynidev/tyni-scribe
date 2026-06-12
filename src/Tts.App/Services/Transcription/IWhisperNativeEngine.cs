namespace Tts.App.Services.Transcription;

public interface IWhisperNativeEngine : IDisposable
{
    Task<string> TranscribeAsync(WhisperNativeTranscriptionRequest request, CancellationToken cancellationToken = default);
}