namespace Tts.App.Services.Transcription;

public interface IFasterWhisperNativeEngine : IDisposable
{
    Task<string> TranscribeAsync(FasterWhisperNativeTranscriptionRequest request, CancellationToken cancellationToken = default);
}