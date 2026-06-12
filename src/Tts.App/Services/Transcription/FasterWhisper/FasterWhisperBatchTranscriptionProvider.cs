using System.IO;
using Tts.App.Services;

namespace Tts.App.Services.Transcription;

public sealed class FasterWhisperBatchTranscriptionProvider : IBatchTranscriptionProvider
{
    public const string ProviderId = "faster-whisper-local";

    private readonly IFasterWhisperNativeEngine _engine;

    public FasterWhisperBatchTranscriptionProvider(IFasterWhisperNativeEngine engine)
    {
        _engine = engine;
    }

    public TranscriptionProviderMetadata Metadata { get; } = new(
        ProviderId,
        "faster-whisper local GPU",
        TranscriptionMode.Batch,
        RequiresEndpoint: false,
        "Runs a local CTranslate2/faster-whisper native GPU provider when installed.");

    public IReadOnlyList<ProviderSettingDescriptor> SettingDescriptors => FasterWhisperProviderSettings.Descriptors;

    public async Task<BatchTranscriptionResult> TranscribeAsync(BatchTranscriptionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!File.Exists(request.AudioFilePath))
        {
            throw new FileNotFoundException("The completed recording is not available for transcription.");
        }

        var settings = FasterWhisperProviderSettings.Parse(request.Settings);
        var modelDirectory = FasterWhisperRuntimePaths.ResolveModelDirectory(settings);
        if (string.IsNullOrWhiteSpace(modelDirectory) || !Directory.Exists(modelDirectory))
        {
            throw new DirectoryNotFoundException($"The selected faster-whisper model '{settings.ModelId}' is not installed or was not found.");
        }

        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        try
        {
            var text = await _engine.TranscribeAsync(
                new FasterWhisperNativeTranscriptionRequest(
                    settings.ModelId,
                    modelDirectory,
                    request.AudioFilePath,
                    settings.Language,
                    settings.ComputeType,
                    settings.TimeoutSeconds),
                timeoutCancellation.Token);

            return new BatchTranscriptionResult(text.Trim());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("faster-whisper local GPU transcription timed out.");
        }
    }
}