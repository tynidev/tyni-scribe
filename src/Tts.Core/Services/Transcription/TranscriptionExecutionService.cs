using System.Diagnostics;
using NAudio.Wave;
using Tts.Core.Configuration;
using Tts.Core.Services.Audio;
using Tts.Core.Services.AudioProcessing;

namespace Tts.Core.Services.Transcription;

public sealed class TranscriptionExecutionService : ITranscriptionExecutionService
{
    private readonly IEnumerable<IAudioProcessingProvider> _audioProcessors;
    private readonly IEnumerable<IBatchTranscriptionProvider> _transcriptionProviders;

    public TranscriptionExecutionService(
        IEnumerable<IAudioProcessingProvider> audioProcessors,
        IEnumerable<IBatchTranscriptionProvider> transcriptionProviders)
    {
        _audioProcessors = audioProcessors;
        _transcriptionProviders = transcriptionProviders;
    }

    public async Task<TranscriptionExecutionResult> TranscribeAsync(TranscriptionExecutionRequest request, CancellationToken cancellationToken = default)
    {
        // Validate the request and normalize the input audio path before doing any provider work.
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Settings);
        var audioPath = Path.GetFullPath(request.AudioFilePath);
        if (!File.Exists(audioPath))
        {
            throw new FileNotFoundException("The audio file was not found.");
        }

        // ReadSupportedWavInfo verifies that the input is a WAV format this pipeline can process and returns its format metadata.
        // Must be a valid WAV file with the following characteristics:
        // - SampleRate = 16 kHz
        // - Channels = 1
        // - BitsPerSample = 16
        var audioInfo = ReadSupportedWavInfo(audioPath);

        // ResolveProviders in this order:
        // - from the request
        // - saved settings
        // - defaults
        var providers = ResolveProviders(request);

        // Resolve Transcription Settings:
        // - Loads Saved settings
        // - Applies setting overrides from the request
        var transcriptionSettings = ResolveTranscriptionSettings(request.Settings, providers.TranscriptionProviderId, request.SettingOverrides ?? new Dictionary<string, string>());

        // GetProviderSettings from the saved settings.
        var audioProcessorSettings = GetProviderSettings(request.Settings.AudioProcessingProviderSettings, providers.AudioProcessorProviderId);

        // Process the source WAV through the selected audio processor.
        var audioProcessingStopwatch = Stopwatch.StartNew();
        var processedAudio = await providers.AudioProcessor.ProcessAsync(
            new AudioProcessingRequest(audioPath, audioInfo.Format, audioInfo.Duration, audioProcessorSettings),
            cancellationToken);
        audioProcessingStopwatch.Stop();

        try
        {
            // Run transcription against the processed audio while measuring provider latency.
            var transcriptionStopwatch = Stopwatch.StartNew();
            var result = await providers.TranscriptionProvider.TranscribeAsync(
                new BatchTranscriptionRequest(processedAudio.FilePath, audioInfo.Format, audioInfo.Duration, transcriptionSettings),
                cancellationToken);
            transcriptionStopwatch.Stop();

            // Return final text plus the provider IDs, effective settings, source audio metadata, and timing metrics.
            return new TranscriptionExecutionResult(
                result.Text,
                providers.TranscriptionProviderId,
                providers.AudioProcessorProviderId,
                transcriptionSettings,
                audioPath,
                audioInfo.Duration.TotalSeconds,
                ToMilliseconds(audioProcessingStopwatch.Elapsed),
                ToMilliseconds(transcriptionStopwatch.Elapsed),
                CalculateRealTimeFactor(audioInfo.Duration, transcriptionStopwatch.Elapsed),
                CalculateAudioSecondsPerSecond(audioInfo.Duration, transcriptionStopwatch.Elapsed));
        }
        finally
        {
            // Remove temporary processed audio files, but never delete the original user-provided input file.
            if (!processedAudio.IsOriginalFile)
            {
                DeleteIfExists(processedAudio.FilePath);
            }
        }
    }

    private ResolvedProviders ResolveProviders(TranscriptionExecutionRequest request)
    {
        var transcriptionProviderId = string.IsNullOrWhiteSpace(request.ProviderId)
            ? request.Settings.SelectedTranscriptionProviderId
            : request.ProviderId.Trim();
        var audioProcessorProviderId = string.IsNullOrWhiteSpace(request.AudioProcessorProviderId)
            ? request.Settings.SelectedAudioProcessorProviderId
            : request.AudioProcessorProviderId.Trim();

        // If no provider ID is specified, use the default provider IDs.
        if (string.IsNullOrWhiteSpace(transcriptionProviderId))
        {
            transcriptionProviderId = AppSettings.DefaultTranscriptionProviderId;
        }
        if (string.IsNullOrWhiteSpace(audioProcessorProviderId))
        {
            audioProcessorProviderId = AppSettings.DefaultAudioProcessorProviderId;
        }

        return new ResolvedProviders(
            transcriptionProviderId,
            audioProcessorProviderId,
            ResolveAudioProcessor(audioProcessorProviderId),
            ResolveTranscriptionProvider(transcriptionProviderId));
    }

    private IAudioProcessingProvider ResolveAudioProcessor(string audioProcessorProviderId)
    {
        var audioProcessor = _audioProcessors.FirstOrDefault(provider => provider.Metadata.Id.Equals(audioProcessorProviderId, StringComparison.OrdinalIgnoreCase));
        return audioProcessor ?? throw new InvalidOperationException($"Audio processor '{audioProcessorProviderId}' is not available.");
    }

    private IBatchTranscriptionProvider ResolveTranscriptionProvider(string providerId)
    {
        var provider = _transcriptionProviders.FirstOrDefault(candidate => candidate.Metadata.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));
        return provider ?? throw new InvalidOperationException($"Transcription provider '{providerId}' is not available.");
    }

    private static Dictionary<string, string> ResolveTranscriptionSettings(
        AppSettings settings,
        string providerId,
        IReadOnlyDictionary<string, string> overrides)
    {
        var effectiveSettings = new Dictionary<string, string>(GetProviderSettings(settings.TranscriptionProviderSettings, providerId), StringComparer.OrdinalIgnoreCase);
        foreach (var pair in overrides)
        {
            effectiveSettings[pair.Key] = pair.Value;
        }

        return effectiveSettings;
    }

    private static IReadOnlyDictionary<string, string> GetProviderSettings(
        Dictionary<string, Dictionary<string, string>> providerSettings,
        string providerId)
    {
        foreach (var pair in providerSettings)
        {
            if (pair.Key.Equals(providerId, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static WavInfo ReadSupportedWavInfo(string audioPath)
    {
        try
        {
            using var reader = new WaveFileReader(audioPath);
            var format = reader.WaveFormat;
            if (format.SampleRate != 16000 || format.Channels != 1 || format.BitsPerSample != 16 || !IsPcmWav(format))
            {
                throw new InvalidOperationException("Transcription currently supports 16 kHz mono PCM 16-bit WAV input. Convert the audio before transcription.");
            }

            return new WavInfo(AudioCaptureFormat.FromWaveFormat(format), reader.TotalTime);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or FormatException or NotSupportedException)
        {
            throw new InvalidOperationException("The audio file could not be read as a supported WAV file.", exception);
        }
    }

    private static bool IsPcmWav(WaveFormat format)
    {
        return format.Encoding is WaveFormatEncoding.Pcm or WaveFormatEncoding.Extensible;
    }

    private static long ToMilliseconds(TimeSpan duration)
    {
        return (long)Math.Round(duration.TotalMilliseconds, MidpointRounding.AwayFromZero);
    }

    private static double? CalculateRealTimeFactor(TimeSpan audioDuration, TimeSpan elapsed)
    {
        if (audioDuration.TotalSeconds <= 0)
        {
            return null;
        }

        return Math.Round(elapsed.TotalSeconds / audioDuration.TotalSeconds, 6, MidpointRounding.AwayFromZero);
    }

    private static double? CalculateAudioSecondsPerSecond(TimeSpan audioDuration, TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds <= 0)
        {
            return null;
        }

        return Math.Round(audioDuration.TotalSeconds / elapsed.TotalSeconds, 6, MidpointRounding.AwayFromZero);
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record WavInfo(AudioCaptureFormat Format, TimeSpan Duration);

    private sealed record ResolvedProviders(
        string TranscriptionProviderId,
        string AudioProcessorProviderId,
        IAudioProcessingProvider AudioProcessor,
        IBatchTranscriptionProvider TranscriptionProvider);
}