using System.IO;
using Tts.Core.Services;

namespace Tts.Core.Services.Transcription;

public sealed class WhisperNativeBatchTranscriptionProvider : IBatchTranscriptionProvider
{
    public const string ProviderId = "whisper-cpp-native-local";

    private readonly IWhisperNativeEngine _engine;

    public WhisperNativeBatchTranscriptionProvider(IWhisperNativeEngine engine)
    {
        _engine = engine;
    }

    public TranscriptionProviderMetadata Metadata { get; } = new(
        ProviderId,
        "whisper.cpp native local",
        TranscriptionMode.Batch,
        RequiresEndpoint: false,
        "Runs the in-process native whisper.cpp interop engine.");

    public IReadOnlyList<ProviderSettingDescriptor> SettingDescriptors => WhisperNativeProviderSettings.Descriptors;

    public async Task<BatchTranscriptionResult> TranscribeAsync(BatchTranscriptionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!File.Exists(request.AudioFilePath))
        {
            throw new FileNotFoundException("The completed recording is not available for transcription.");
        }

        var settings = WhisperNativeProviderSettings.Parse(request.Settings);
        var modelPath = WhisperCppRuntimePaths.ResolveModelPath(settings);
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            throw new FileNotFoundException($"The selected local Whisper model '{settings.ModelId}' is not installed or was not found.");
        }

        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        try
        {
            var text = await _engine.TranscribeAsync(
                new WhisperNativeTranscriptionRequest(
                    settings.ModelId,
                    modelPath,
                    request.AudioFilePath,
                    settings.Language,
                    settings.TimeoutSeconds),
                timeoutCancellation.Token);

            return new BatchTranscriptionResult(text.Trim());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Native whisper.cpp transcription timed out.");
        }
    }
}
