using System.IO;

namespace Tts.App.Services.Transcription;

public sealed class WhisperWarmBatchTranscriptionProvider : IBatchTranscriptionProvider
{
    public const string ProviderId = "whisper-cpp-warm-local";

    private readonly IWhisperWarmEngine _engine;

    public WhisperWarmBatchTranscriptionProvider(IWhisperWarmEngine engine)
    {
        _engine = engine;
    }

    public TranscriptionProviderMetadata Metadata { get; } = new(
        ProviderId,
        "whisper.cpp warm local",
        TranscriptionMode.Batch,
        RequiresEndpoint: false);

    public async Task<BatchTranscriptionResult> TranscribeAsync(BatchTranscriptionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!File.Exists(request.AudioFilePath))
        {
            throw new FileNotFoundException("The completed recording is not available for transcription.");
        }

        var modelPath = WhisperCppRuntimePaths.ResolveModelPath(request.Settings);
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            throw new FileNotFoundException($"The selected local Whisper model '{request.Settings.WhisperCppModelId}' is not installed or was not found.");
        }

        var serverExecutablePath = WhisperCppRuntimePaths.ResolveServerExecutablePath(request.Settings);
        if (string.IsNullOrWhiteSpace(serverExecutablePath) || !File.Exists(serverExecutablePath))
        {
            throw new FileNotFoundException("The warm local Whisper engine is not installed or was not found.");
        }

        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(TimeSpan.FromSeconds(request.Settings.TimeoutSeconds));

        try
        {
            var text = await _engine.TranscribeAsync(
                new WhisperWarmTranscriptionRequest(
                    request.Settings.WhisperCppModelId,
                    modelPath,
                    serverExecutablePath,
                    request.AudioFilePath,
                    request.Settings.Language,
                    request.Settings.TimeoutSeconds),
                timeoutCancellation.Token);

            return new BatchTranscriptionResult(text.Trim());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Warm local whisper.cpp transcription timed out.");
        }
    }
}