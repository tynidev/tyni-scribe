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

        var modelDirectory = FasterWhisperRuntimePaths.ResolveModelDirectory(request.Settings);
        if (string.IsNullOrWhiteSpace(modelDirectory) || !Directory.Exists(modelDirectory))
        {
            throw new DirectoryNotFoundException($"The selected faster-whisper model '{request.Settings.FasterWhisperModelId}' is not installed or was not found.");
        }

        var computeType = string.IsNullOrWhiteSpace(request.Settings.FasterWhisperComputeType)
            ? FasterWhisperProviderSettings.DefaultComputeType
            : request.Settings.FasterWhisperComputeType.Trim();

        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(TimeSpan.FromSeconds(request.Settings.TimeoutSeconds));

        try
        {
            var text = await _engine.TranscribeAsync(
                new FasterWhisperNativeTranscriptionRequest(
                    request.Settings.FasterWhisperModelId,
                    modelDirectory,
                    request.AudioFilePath,
                    request.Settings.Language,
                    computeType,
                    request.Settings.TimeoutSeconds),
                timeoutCancellation.Token);

            return new BatchTranscriptionResult(text.Trim());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("faster-whisper local GPU transcription timed out.");
        }
    }
}