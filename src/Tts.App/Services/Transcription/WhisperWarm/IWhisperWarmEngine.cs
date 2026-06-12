namespace Tts.App.Services.Transcription;

public interface IWhisperWarmEngine : IDisposable
{
    Task<string> TranscribeAsync(WhisperWarmTranscriptionRequest request, CancellationToken cancellationToken = default);
}